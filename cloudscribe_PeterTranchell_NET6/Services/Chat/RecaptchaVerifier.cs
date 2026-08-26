using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using cloudscribe.Core.Web.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class RecaptchaVerifier
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<RecaptchaVerifier> _logger;
        private static int _resolutionLogged;

        public RecaptchaVerifier(
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ILogger<RecaptchaVerifier> logger)
        {
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _logger = logger;
        }

        private async Task<(string PublicKey, string PrivateKey)> ResolveKeys()
        {
            var publicKey = _configuration["RecaptchaKeys:PublicKey"];
            var privateKey = _configuration["RecaptchaKeys:PrivateKey"];

            try
            {
                var context = _httpContextAccessor.HttpContext;
                var resolver = context?.RequestServices?
                    .GetService(typeof(ISiteContextResolver)) as ISiteContextResolver;
                if (context != null && resolver != null)
                {
                    var site = await resolver
                        .ResolveSite(context.Request.Host.Host, context.Request.Path)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(site?.RecaptchaPublicKey))
                    {
                        publicKey = site.RecaptchaPublicKey;
                    }
                    if (!string.IsNullOrWhiteSpace(site?.RecaptchaPrivateKey))
                    {
                        privateKey = site.RecaptchaPrivateKey;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return (publicKey ?? string.Empty, privateKey ?? string.Empty);
        }

        public async Task<bool> IsConfiguredAsync()
        {
            var keys = await ResolveKeys().ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(keys.PrivateKey);
        }

        public async Task LogResolutionOnceAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _resolutionLogged, 1) == 1) return;
            var keys = await ResolveKeys().ConfigureAwait(false);
            _logger.LogWarning(
                "Chat reCAPTCHA resolution (first request): publicKeyPresent={Pub}, privateKeyPresent={Priv}, source={Source}",
                keys.PublicKey.Length > 0,
                keys.PrivateKey.Length > 0,
                keys.PublicKey.Length > 0 || keys.PrivateKey.Length > 0 ? "resolved" : "none");
        }

        public async Task<bool> VerifyAsync(string token, string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Chat reCAPTCHA rejected: missing token");
                return false;
            }
            try
            {
                var client = _httpClientFactory.CreateClient("RecaptchaVerify");
                client.Timeout = TimeSpan.FromSeconds(10);

                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = (await ResolveKeys().ConfigureAwait(false)).PrivateKey,
                    ["response"] = token,
                    ["remoteip"] = remoteIp ?? string.Empty
                });

                var response = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify", form).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Chat reCAPTCHA siteverify returned {Status}: {Body}; rejecting", (int)response.StatusCode, body);
                    return false;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var success = root.TryGetProperty("success", out var successEl) && successEl.ValueKind == JsonValueKind.True;

                string score = null;
                string action = null;
                if (root.TryGetProperty("score", out var scoreEl)) score = scoreEl.ToString();
                if (root.TryGetProperty("action", out var actionEl)) action = actionEl.ToString();

                if (!success)
                {
                    var codes = root.TryGetProperty("error-codes", out var codesEl) && codesEl.ValueKind == JsonValueKind.Array
                        ? JsonSerializer.Deserialize<List<string>>(codesEl.GetRawText())
                        : null;
                    _logger.LogWarning("Chat reCAPTCHA verification failed (success=false, action={Action}, errors={Errors}); rejecting",
                        action ?? "n/a", codes != null ? string.Join(",", codes) : "none");
                    return false;
                }

                if (!string.IsNullOrEmpty(action) && action != "chat")
                {
                    _logger.LogWarning("Chat reCAPTCHA action mismatch ({Action}); rejecting", action);
                    return false;
                }

                _logger.LogInformation("Chat reCAPTCHA verified: score={Score}, action={Action}", score ?? "n/a", action ?? "n/a");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat reCAPTCHA verification errored; rejecting");
                return false;
            }
        }
    }
}
