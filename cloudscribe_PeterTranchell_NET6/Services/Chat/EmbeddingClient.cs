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
    public class EmbeddingClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ChatOptions _options;
        private readonly ILogger<EmbeddingClient> _logger;

        public EmbeddingClient(
            IHttpClientFactory httpClientFactory,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<EmbeddingClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        public bool IsConfigured => _options.IsConfigured() && !string.IsNullOrWhiteSpace(_options.EmbeddingDeployment);

        public async Task<float[]> EmbedAsync(string input)
        {
            var batches = await EmbedBatchesAsync(new[] { input }).ConfigureAwait(false);
            return batches[0];
        }

        public async Task<float[][]> EmbedBatchesAsync(IReadOnlyList<string> inputs)
        {
            var results = new float[inputs.Count][];
            var batchSize = Math.Max(1, _options.EmbedBatchSize);

            for (var offset = 0; offset < inputs.Count; offset += batchSize)
            {
                var count = Math.Min(batchSize, inputs.Count - offset);
                var batch = new string[count];
                for (var i = 0; i < count; i++) batch[i] = inputs[offset + i];

                var vectors = await EmbedOneBatchAsync(batch).ConfigureAwait(false);
                for (var i = 0; i < count; i++)
                {
                    results[offset + i] = vectors != null && i < vectors.Length ? vectors[i] : null;
                }
            }

            var nonEmpty = results.Count(v => v != null && v.Length > 0);
            _logger.LogInformation(
                "Embedding batches complete: {Received}/{Total} non-empty vectors",
                nonEmpty, inputs.Count);

            return results;
        }

        private async Task<float[][]> EmbedOneBatchAsync(string[] batch)
        {
            var client = _httpClientFactory.CreateClient("ChatEmbeddings");
            var url = AzureOpenAi.BuildUrl(_options, _options.EmbeddingDeployment, "/openai/deployments/", "/embeddings?api-version=");

            try
            {
                var response = await HttpRetry.SendWithRetryAsync(client, () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
                    request.Content = HttpRetry.ToJsonContent(new { input = batch });
                    return request;
                }).ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Embedding request failed: {Error}", HttpRetry.DescribeError(response, body));
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.GetProperty("data");
                var results = new float[data.GetArrayLength()][];
                foreach (var item in data.EnumerateArray())
                {
                    var index = item.GetProperty("index").GetInt32();
                    var embedding = item.GetProperty("embedding");
                    var vector = new float[embedding.GetArrayLength()];
                    var i = 0;
                    foreach (var value in embedding.EnumerateArray())
                    {
                        vector[i++] = value.GetSingle();
                    }
                    if (index >= 0 && index < results.Length) results[index] = vector;
                }

                var usable = results.Count(v => v != null && v.Length > 0);
                if (usable < results.Length)
                {
                    _logger.LogWarning(
                        "Embedding batch returned {Usable}/{Total} usable vectors (first vector dims: {Dims})",
                        usable, results.Length,
                        results.Length > 0 && results[0] != null ? results[0].Length : -1);
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding request threw an exception");
                return null;
            }
        }
    }

    public static class AzureOpenAi
    {
        public static string BuildUrl(ChatOptions options, string deployment, string prefix, string suffix)
        {
            var baseUrl = options.BaseUrl.TrimEnd('/');
            return baseUrl
                + prefix
                + Uri.EscapeDataString(deployment)
                + suffix
                + Uri.EscapeDataString(options.ApiVersion);
        }
    }
}
