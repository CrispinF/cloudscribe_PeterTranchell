using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using cloudscribe_PeterTranchell_NET6.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Controllers
{
    [ApiController]
    [Produces("application/json")]
    public class ChatApiController : ControllerBase
    {
        private const int MaxMessages = 24;
        private const int MaxTotalChars = 16000;

        private readonly ChatIndexService _indexService;
        private readonly LlmChatClient _llm;
        private readonly RecaptchaVerifier _recaptcha;
        private readonly ChatNonceService _nonce;
        private readonly ChatOptions _options;
        private readonly ILogger<ChatApiController> _logger;

        public ChatApiController(
            ChatIndexService indexService,
            LlmChatClient llm,
            RecaptchaVerifier recaptcha,
            ChatNonceService nonce,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<ChatApiController> logger)
        {
            _indexService = indexService;
            _llm = llm;
            _recaptcha = recaptcha;
            _nonce = nonce;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        [HttpPost("api/chat")]
        public async Task<IActionResult> Post([FromBody] ChatRequestDto request)
        {
            if (!_options.Enabled) return NotFound();
            if (!_options.IsConfigured())
            {
                return StatusCode(503, new { error = "The assistant is not configured yet. Please check back soon." });
            }

            if (!IsSameOrigin()) return Forbid();

            if (_options.NonceEnabled)
            {
                if (!_nonce.TryValidateToken(request?.Nonce ?? string.Empty, _options.NonceMaxAgeSeconds, out var nonceError))
                {
                    _logger.LogWarning("Chat nonce rejected: {Reason}", nonceError);
                    return StatusCode(403, new { error = "Verification failed. Please refresh the page and try again." });
                }
            }
            else
            {
                await _recaptcha.LogResolutionOnceAsync();

                if (_options.UseRecaptcha && await _recaptcha.IsConfiguredAsync())
                {
                    var ok = await _recaptcha.VerifyAsync(request?.CaptchaToken ?? string.Empty, RemoteIp());
                    if (!ok)
                    {
                        return StatusCode(403, new { error = "Verification failed. Please try again." });
                    }
                }
            }

            var question = ExtractQuestion(request);
            if (question == null)
            {
                return BadRequest(new { error = "Please type a question first." });
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var history = SanitizeHistory(request.Messages);

                ChatCompletionResult rewriteResult = null;
                var searchQuery = question;
                if (history.Count > 0)
                {
                    rewriteResult = await _llm.RewriteQueryAsync(history, question).ConfigureAwait(false);
                    searchQuery = rewriteResult?.Content ?? question;
                }
                if (!string.Equals(searchQuery, question, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Chat retrieval query rewritten to: {Query}", searchQuery);
                }

                var context = await _indexService.RetrieveAsync(searchQuery, Math.Max(3, _options.TopK));
                if (context.Chunks.Count == 0)
                {
                    return Ok(new ChatResponseDto
                    {
                        Reply = "I couldn't find any site content relevant to that question. Try rephrasing it, or browse the Music and Writings sections.",
                        Sources = new List<ChatSourceDto>()
                    });
                }

                var completion = await _llm.CompleteAsync(history, question, context);
                if (completion == null || string.IsNullOrWhiteSpace(completion.Content))
                {
                    return StatusCode(502, new { error = "The assistant is temporarily unavailable. Please try again in a moment." });
                }

                stopwatch.Stop();
                _logger.LogInformation(
                    "Chat served: question=\"{Question}\" reply=\"{Reply}\" promptTokens={PromptTokens} completionTokens={CompletionTokens} totalTokens={TotalTokens} sources={SourceCount} elapsedMs={ElapsedMs}",
                    question,
                    completion.Content,
                    completion.PromptTokens + (rewriteResult?.PromptTokens ?? 0),
                    completion.CompletionTokens + (rewriteResult?.CompletionTokens ?? 0),
                    completion.TotalTokens + (rewriteResult?.TotalTokens ?? 0),
                    context.Sources.Count,
                    stopwatch.ElapsedMilliseconds);

                return Ok(new ChatResponseDto { Reply = completion.Content, Sources = context.Sources });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat request failed");
                return StatusCode(502, new { error = "The assistant is temporarily unavailable. Please try again in a moment." });
            }
        }

        [HttpGet("api/chat/nonce")]
        public IActionResult GetNonce([FromQuery(Name = "current")] string current)
        {
            if (!_options.Enabled) return NotFound();
            if (!_options.NonceEnabled) return NotFound();
            if (!IsSameOrigin()) return Forbid();

            if (_nonce.TryRefreshToken(current ?? string.Empty, _options.NonceMaxAgeSeconds, _options.NonceRefreshGraceSeconds, _options.NonceMaxRefreshCount, out var newToken, out var error))
            {
                return Ok(new { nonce = newToken });
            }

            _logger.LogWarning("Chat nonce refresh rejected: {Reason}", error);
            return StatusCode(403, new { error = "Verification failed. Please refresh the page and try again." });
        }

        private string ExtractQuestion(ChatRequestDto request)
        {
            if (request == null || request.Messages == null) return null;
            var lastUser = request.Messages.LastOrDefault(m => m != null
                && !string.IsNullOrWhiteSpace(m.Content)
                && m.Role != null
                && m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
            if (lastUser == null) return null;
            var content = lastUser.Content.Trim();
            if (content.Length > _options.MaxMessageChars) content = content.Substring(0, _options.MaxMessageChars);
            return content.Length == 0 ? null : content;
        }

        private List<ChatMessageDto> SanitizeHistory(List<ChatMessageDto> messages)
        {
            var result = new List<ChatMessageDto>();
            if (messages == null) return result;

            foreach (var message in messages)
            {
                if (message == null || string.IsNullOrWhiteSpace(message.Content)) continue;
                var role = message.Role != null && message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? "assistant"
                    : "user";
                var content = message.Content.Trim();
                if (content.Length > _options.MaxMessageChars) content = content.Substring(0, _options.MaxMessageChars);
                result.Add(new ChatMessageDto { Role = role, Content = content });
            }

            if (result.Count > 1) result.RemoveAt(result.Count - 1);

            while (result.Count > MaxMessages - 1) result.RemoveAt(0);

            var total = result.Sum(m => m.Content.Length);
            while (total > MaxTotalChars && result.Count > 0)
            {
                total -= result[0].Content.Length;
                result.RemoveAt(0);
            }

            return result;
        }

        private bool IsSameOrigin()
        {
            if (!Request.Headers.ContainsKey("Origin")) return true;
            var origin = Request.Headers["Origin"].ToString();
            if (string.IsNullOrEmpty(origin)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        private string RemoteIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }
    }
}
