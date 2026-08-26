using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class LlmChatClient
    {
        private const string DefaultSystemPrompt =
            "You are the helpful on-site assistant for peter-tranchell.uk, the website of the Peter Tranchell Foundation, " +
            "celebrating the composer Peter Andrew Tranchell (1922-1993). " +
            "Answer the user's question using the numbered website extracts provided in the current message. " +
            "Rules: " +
            "1) Base every factual statement on those extracts; never invent facts, titles, dates or works. " +
            "2) Support your answer with markdown links to the exact page URLs given in the extracts, e.g. [page title](URL). " +
            "3) Synthesize information across several extracts when together they answer the question. " +
            "Note: extracts from the Classified Handlist are works lists under numbered category headings (such as '3d - Hymns'); " +
            "titles enumerated beneath such a heading are works of that type, so a list of tunes under 'Hymns' means he wrote hymn tunes. " +
            "4) If the extracts do not contain enough information, say so briefly, then share any closely related details the extracts DO contain and link the most relevant pages. " +
            "5) Be clear and friendly; typically under 220 words, using short paragraphs or lists where helpful. " +
            "6) Write in English.";

        private const string RewriteSystemPrompt =
            "Rewrite the user's latest question as one standalone keyword search query about the content of the " +
            "Peter Tranchell Foundation website (composer Peter Andrew Tranchell, his music, life and works), " +
            "resolving any pronouns or references to earlier turns using the conversation. " +
            "Use only distinctive subject keywords - names of people, works, instruments, venues, events and topics. " +
            "Never include the site or organisation name ('Peter Tranchell Foundation'), 'website', or similar navigation or branding words. " +
            "Reply with ONLY the rewritten query text - no quotes, no punctuation at the end, no explanation.";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ChatOptions _options;
        private readonly ILogger<LlmChatClient> _logger;

        public LlmChatClient(
            IHttpClientFactory httpClientFactory,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<LlmChatClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        public async Task<string> RewriteQueryAsync(
            IReadOnlyList<ChatMessageDto> history,
            string question)
        {
            try
            {
                var messages = new List<object> { new { role = "system", content = RewriteSystemPrompt } };
                foreach (var message in history.Skip(Math.Max(0, history.Count - 6)))
                {
                    var role = message.Role == "assistant" ? "assistant" : "user";
                    messages.Add(new { role, content = Truncate(message.Content, 500) });
                }
                messages.Add(new { role = "user", content = question });

                var reply = await SendMessagesAsync(messages, 200).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(reply)) return null;
                return reply.Trim().Trim('"', '\'', '`').Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat query rewrite failed; falling back to raw question");
                return null;
            }
        }

        public async Task<string> CompleteAsync(
            IReadOnlyList<ChatMessageDto> history,
            string question,
            RetrievalResult context)
        {
            var systemPrompt = string.IsNullOrWhiteSpace(_options.SystemPrompt)
                ? DefaultSystemPrompt
                : _options.SystemPrompt;
            var messages = new List<object> { new { role = "system", content = systemPrompt } };
            foreach (var message in history)
            {
                var role = message.Role == "assistant" ? "assistant" : "user";
                messages.Add(new { role, content = Truncate(message.Content, 2000) });
            }
            messages.Add(new { role = "user", content = BuildUserMessage(question, context) });

            return await SendMessagesAsync(messages, _options.MaxOutputTokens).ConfigureAwait(false);
        }

        private async Task<string> SendMessagesAsync(List<object> messages, int maxOutputTokens)
        {
            var client = _httpClientFactory.CreateClient("ChatLlm");
            var url = AzureOpenAi.BuildUrl(_options, _options.ChatDeployment, "/openai/deployments/", "/chat/completions?api-version=");

            object payload = maxOutputTokens > 0
                ? new { messages, max_completion_tokens = maxOutputTokens }
                : (object)new { messages };

            var response = await HttpRetry.SendWithRetryAsync(client, () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
                request.Content = HttpRetry.ToJsonContent(payload);
                return request;
            }).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Chat completion failed: {Error}", HttpRetry.DescribeError(response, body));
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;
            var first = choices[0];
            var content = string.Empty;
            if (first.TryGetProperty("message", out var messageElement)
                && messageElement.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                content = contentElement.GetString() ?? string.Empty;
            }
            if (string.IsNullOrEmpty(content)) return null;
            if (first.TryGetProperty("finish_reason", out var finishElement)
                && finishElement.ValueKind == JsonValueKind.String
                && finishElement.GetString() == "length")
            {
                content += "\n\n[... response truncated due to length; try asking a more specific question]";
            }
            return content;
        }

        private static string BuildUserMessage(string question, RetrievalResult context)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Website extracts:");
            for (var i = 0; i < context.Chunks.Count; i++)
            {
                var chunk = context.Chunks[i];
                sb.Append('[').Append(i + 1).Append("] ").AppendLine(chunk.Title);
                sb.AppendLine(chunk.Url);
                sb.AppendLine(Truncate(chunk.Text.Replace('\n', ' '), 1200));
                sb.AppendLine();
            }
            sb.Append("Question: ").Append(question);
            return sb.ToString();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
