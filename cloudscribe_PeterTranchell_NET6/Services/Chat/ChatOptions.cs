using System;
using System.Collections.Generic;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public enum CorpusSource
    {
        Database = 0,
        Lunr = 1
    }

    public class ChatOptions
    {
        public bool Enabled { get; set; } = true;

        public CorpusSource Source { get; set; } = CorpusSource.Database;

        /// <summary>
        /// The Azure AI Foundry endpoint base URL for embeddings/chat calls.
        /// Not the public website origin - use <see cref="SiteBaseUrl"/> for that.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// The origin (scheme + host) of the public website, used to build absolute
        /// content URLs in the corpus, e.g. "https://peter-tranchell.uk".
        /// Distinct from <see cref="BaseUrl"/> (the Azure AI Foundry endpoint).
        /// </summary>
        public string SiteBaseUrl { get; set; } = "https://peter-tranchell.uk";

        public string BlogPathRoot { get; set; } = "blog";

        public Dictionary<string, string> BlogPathBySiteFolder { get; set; } = new Dictionary<string, string>();

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

        /// <summary>
        /// When true, the server embeds a signed nonce in the chat panel page.
        /// The client must send it back with every request. The server rejects
        /// requests whose nonce is missing, tampered with, too fresh (&lt;2 s),
        /// or older than <see cref="NonceMaxAgeSeconds"/>.
        /// </summary>
        public bool NonceEnabled { get; set; } = true;

        /// <summary>
        /// Maximum acceptable age (in seconds) for a nonce before it expires.
        /// Default 600 (10 minutes).
        /// </summary>
        public int NonceMaxAgeSeconds { get; set; } = 600;

        /// <summary>
        /// Extra time (in seconds) beyond <see cref="NonceMaxAgeSeconds"/> during
        /// which an expired nonce may still be rolled into a fresh one via
        /// GET /api/chat/nonce. Default 1800 (30 minutes).
        /// </summary>
        public int NonceRefreshGraceSeconds { get; set; } = 1800;

        /// <summary>
        /// Maximum number of times a single nonce chain may be refreshed.
        /// Default 5 - prevents one page-fetched nonce from being farmed
        /// indefinitely through the refresh endpoint.
        /// </summary>
        public int NonceMaxRefreshCount { get; set; } = 5;

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
