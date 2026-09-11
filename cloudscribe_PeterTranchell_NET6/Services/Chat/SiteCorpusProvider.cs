using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class SiteCorpusProvider : ICorpusProvider
    {
        private const string IndexFolder = "lunr-index";
        private static readonly Regex CssAtRule = new Regex(@"@[a-zA-Z-]+[\w\s-]*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex CssRule = new Regex(@"[.#]?[A-Za-z][\w\-.,:#\[\]=""'()\s>~+*^|$]{0,160}?\{[^{}]{0,500}?[:;][^{}]{0,500}?\}", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly HashSet<string> ExcludedSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "search", "lunr-search", "google-search", "old-google-search", "sitemap", "rss"
        };

        private readonly IWebHostEnvironment _env;
        private readonly ChatOptions _options;
        private readonly ILogger<SiteCorpusProvider> _logger;

        public SiteCorpusProvider(IWebHostEnvironment env, IOptions<ChatOptions> optionsAccessor, ILogger<SiteCorpusProvider> logger)
        {
            _env = env;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        public Task<CorpusSnapshot> TryLoadAsync()
        {
            return Task.Run(() => TryLoadCore());
        }

        private CorpusSnapshot TryLoadCore()
        {
            try
            {
                var root = _env.WebRootPath;
                if (string.IsNullOrEmpty(root)) root = Path.Combine(_env.ContentRootPath, "wwwroot");
                var folder = Path.Combine(root, IndexFolder);
                var docsPath = Path.Combine(folder, "search-documents.json");
                var versionPath = Path.Combine(folder, "version.txt");
                if (!File.Exists(docsPath))
                {
                    _logger.LogWarning("Chat corpus not found at {DocsPath}", docsPath);
                    return null;
                }

                var version = File.Exists(versionPath) ? File.ReadAllText(versionPath).Trim() : string.Empty;
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                using var stream = File.OpenRead(docsPath);
                var documents = JsonSerializer.Deserialize<List<PageDocument>>(stream, jsonOptions);
                if (documents == null) return null;

                var snapshot = new CorpusSnapshot { Version = version };
                foreach (var doc in documents)
                {
                    if (doc == null || string.IsNullOrWhiteSpace(doc.Id)) continue;
                    if (!string.IsNullOrEmpty(doc.Title) && doc.Title.EndsWith(" - Peter Tranchell Foundation", StringComparison.OrdinalIgnoreCase))
                    {
                        doc.Title = doc.Title.Substring(0, doc.Title.Length - " - Peter Tranchell Foundation".Length);
                    }
                    var cleaned = CleanText(doc.Body ?? string.Empty);
                    if (cleaned.Length < 40) continue;

                    var page = new CorpusPage
                    {
                        Url = doc.Id,
                        Title = string.IsNullOrWhiteSpace(doc.Title) ? doc.Id : doc.Title,
                        Hash = ComputeHash(doc.Title, cleaned)
                    };

                    var bodyChunks = TextChunker.Split(cleaned, Math.Max(400, _options.ChunkChars), Math.Max(0, _options.ChunkOverlapChars));
                    page.ChunkTexts.AddRange(bodyChunks);

                    var meta = (doc.MetaDescription ?? string.Empty).Trim();
                    if (meta.Length > 0)
                    {
                        page.ChunkTexts.Insert(0, "Page summary: " + meta);
                    }
                    else
                    {
                        var intro = cleaned.Length > 300 ? cleaned.Substring(0, 300) : cleaned;
                        if (intro.Length > 0) page.ChunkTexts.Insert(0, intro);
                    }

                    if (page.ChunkTexts.Count > 0) snapshot.Pages.Add(page);
                }

                _logger.LogInformation(
                    "Chat corpus loaded: version {Version}, pages {Pages}",
                    snapshot.Version, snapshot.Pages.Count);
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load chat corpus from lunr-index");
                return null;
            }
        }

        internal static string CleanText(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            var text = body;
            for (var i = 0; i < 3; i++)
            {
                var after = CssAtRule.Replace(text, " ");
                after = CssRule.Replace(after, " ");
                if (after.Length == text.Length) break;
                text = after;
            }
            text = Whitespace.Replace(text, " ").Trim();
            return text;
        }

        internal static string ComputeHash(string title, string body)
        {
            return CorpusText.ComputeHash(title, body);
        }
    }
}
