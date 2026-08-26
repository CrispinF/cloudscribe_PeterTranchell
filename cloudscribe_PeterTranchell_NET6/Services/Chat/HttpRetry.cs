using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public static class HttpRetry
    {
        public static async Task<HttpResponseMessage> SendWithRetryAsync(
            HttpClient client,
            Func<HttpRequestMessage> requestFactory,
            int maxAttempts = 3)
        {
            HttpResponseMessage response = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var request = requestFactory();
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if ((int)response.StatusCode < 500 && (int)response.StatusCode != 429)
                {
                    return response;
                }
                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    response.Dispose();
                    response = null;
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
            return response!;
        }

        public static StringContent ToJsonContent(object payload)
        {
            return new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
        }

        public static string DescribeError(HttpResponseMessage response, string body)
        {
            var detail = body;
            if (!string.IsNullOrEmpty(detail) && detail.Length > 300) detail = detail.Substring(0, 300);
            return $"HTTP {(int)response.StatusCode} {response.StatusCode}: {detail}";
        }
    }
}
