using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cloudscribe.Core.Models;
using cloudscribe.SimpleContent.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class DatabaseCorpusProvider : ICorpusProvider
    {
        private const int MinContentLength = 40;
        private static readonly System.Collections.Generic.HashSet<string> ExcludedSlugs =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "search", "lunr-search", "google-search", "old-google-search", "sitemap", "rss"
            };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ChatOptions _options;
        private readonly ILogger<DatabaseCorpusProvider> _logger;

        public DatabaseCorpusProvider(
            IServiceScopeFactory scopeFactory,
            IOptions<ChatOptions> optionsAccessor,
            ILogger<DatabaseCorpusProvider> logger)
        {
            _scopeFactory = scopeFactory;
            _options = optionsAccessor.Value;
            _logger = logger;
        }

        public async Task<CorpusSnapshot> TryLoadAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var siteQueries = sp.GetRequiredService<ISiteQueries>();
                var pageQueries = sp.GetRequiredService<IPageQueries>();
                var postQueries = sp.GetRequiredService<IPostQueries>();

                var sites = await siteQueries.GetList().ConfigureAwait(false);
                if (sites == null || sites.Count == 0)
                {
                    _logger.LogWarning("Database corpus: no sites found to index");
                    return null;
                }

                var snapshot = new CorpusSnapshot();
                var stampParts = new List<string>();

                foreach (var site in sites)
                {
                    var folderPrefix = BuildFolderPrefix(site);
                    var blogSegment = GetBlogSegment(site);
                    var projectId = site.Id.ToString();

                    var pages = await pageQueries.GetAllPages(projectId).ConfigureAwait(false);
                    if (pages != null)
                    {
                        foreach (var page in pages.Where(p => p != null && p.IsPublished))
                        {
                            if (!IsIndexable(page)) continue;
                            AddPage(snapshot, stampParts, page, site, folderPrefix, postBlogSegment: null);
                        }
                    }

                    var posts = await postQueries.GetPosts(projectId, includeUnpublished: false).ConfigureAwait(false);
                    if (posts != null)
                    {
                        foreach (var post in posts.Where(p => p != null && p.IsPublished))
                        {
                            if (!IsIndexable(post)) continue;
                            AddPage(snapshot, stampParts, post, site, folderPrefix, blogSegment);
                        }
                    }
                }

                snapshot.Version = CorpusText.ComputeStampHash(stampParts.ToArray());

                _logger.LogInformation(
                    "Database corpus loaded: version {Version}, pages {Pages}, sites {Sites} ({Folders})",
                    snapshot.Version, snapshot.Pages.Count, sites.Count,
                    string.Join(",", sites.Select(s => string.IsNullOrEmpty(s.SiteFolderName) ? "<root>" : s.SiteFolderName)));

                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load chat corpus from the content database");
                return null;
            }
        }

        private void AddPage(CorpusSnapshot snapshot, List<string> stampParts, IContentItem item, ISiteInfo site, string folderPrefix, string postBlogSegment)
        {
            var title = (item.Title ?? string.Empty).Trim();
            var body = HtmlToText.Convert(item.Content ?? string.Empty);
            if (body.Length < MinContentLength) return;

            var url = BuildUrl(item.Slug, folderPrefix, postBlogSegment);
            var meta = (item.MetaDescription ?? string.Empty).Trim();

            var page = new CorpusPage
            {
                Url = url,
                Title = string.IsNullOrWhiteSpace(title) ? url : StripSiteSuffix(title),
                Hash = CorpusText.ComputeHash(title, meta + "\n" + body)
            };

            var bodyChunks = TextChunker.Split(body, Math.Max(400, _options.ChunkChars), Math.Max(0, _options.ChunkOverlapChars));
            page.ChunkTexts.AddRange(bodyChunks);

            if (meta.Length > 0)
            {
                page.ChunkTexts.Insert(0, "Page summary: " + meta);
            }
            else
            {
                var intro = body.Length > 300 ? body.Substring(0, 300) : body;
                if (intro.Length > 0) page.ChunkTexts.Insert(0, intro);
            }

            if (page.ChunkTexts.Count > 0)
            {
                snapshot.Pages.Add(page);
                stampParts.Add(site.Id + "|" + page.Title + "|" + meta + "|" + page.Hash);
            }
        }

        private bool IsIndexable(IContentItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Slug)) return false;
            if (ExcludedSlugs.Contains(item.Slug)) return false;
            return true;
        }

        private static string StripSiteSuffix(string title)
        {
            const string suffix = " - Peter Tranchell Foundation";
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return title.Substring(0, title.Length - suffix.Length);
            }
            return title;
        }

        private static string BuildFolderPrefix(ISiteInfo site)
        {
            var folder = site.SiteFolderName;
            if (string.IsNullOrEmpty(folder)) return string.Empty;
            return "/" + folder.Trim('/');
        }

        private string GetBlogSegment(ISiteInfo site)
        {
            var folder = site.SiteFolderName;
            if (!string.IsNullOrEmpty(folder))
            {
                var folderName = folder.Trim('/');
                if (_options.BlogPathBySiteFolder != null
                    && _options.BlogPathBySiteFolder.TryGetValue(folderName, out var seg)
                    && !string.IsNullOrWhiteSpace(seg))
                {
                    return seg.Trim('/');
                }
            }
            return string.IsNullOrWhiteSpace(_options.BlogPathRoot) ? "blog" : _options.BlogPathRoot.Trim('/');
        }

        private string BuildUrl(string slug, string folderPrefix, string postBlogSegment)
        {
            var baseUrl = string.IsNullOrWhiteSpace(_options.SiteBaseUrl)
                ? "https://peter-tranchell.uk"
                : _options.SiteBaseUrl.TrimEnd('/');
            var trimmedSlug = (slug ?? string.Empty).Trim('/');
            if (string.IsNullOrEmpty(trimmedSlug)) return baseUrl + folderPrefix + "/";

            var path = folderPrefix + "/" + trimmedSlug;
            if (!string.IsNullOrEmpty(postBlogSegment))
            {
                path = folderPrefix + "/" + postBlogSegment + "/" + trimmedSlug;
            }
            return baseUrl + path;
        }
    }
}
