using System;
using System.Collections.Generic;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class ChatOptions
    {
        public bool Enabled { get; set; } = true;

        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string ChatDeployment { get; set; } = "gpt-4o-mini";

        public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";

        public string ApiVersion { get; set; } = "2024-10-21";

        public int TopK { get; set; } = 6;

        public int ChunkChars { get; set; } = 1200;

        public int ChunkOverlapChars { get; set; } = 150;

        public int MaxHistoryMessages { get; set; } = 8;

        public int MaxMessageChars { get; set; } = 2000;

        public int MaxOutputTokens { get; set; } = 2000;

        public int EmbedBatchSize { get; set; } = 16;

        public int RefreshIntervalHours { get; set; } = 6;

        public int RateLimitRequestsPerMinute { get; set; } = 10;

        public bool UseRecaptcha { get; set; } = false;

        public string SystemPrompt { get; set; } = string.Empty;

        public bool IsConfigured()
        {
            return Enabled
                && !string.IsNullOrWhiteSpace(BaseUrl)
                && BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(ApiKey)
                && !string.IsNullOrWhiteSpace(ChatDeployment);
        }
    }
}
