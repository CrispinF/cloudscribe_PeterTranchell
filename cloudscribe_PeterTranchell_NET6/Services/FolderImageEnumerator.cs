using cloudscribe.Core.Models;
using cloudscribe.Core.Web.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace cloudscribe_PeterTranchell_NET6.Services
{
    public class FolderImageEnumerator : IFolderImageEnumerator
    {
        // This service is on the request path (the gallery template renders dynamically on every
        // page view), so routine per-render detail is logged at Debug to avoid flooding the console.
        // Warn is reserved for conditions an editor can act on, and Error for genuine failures.
        private const string LogPrefix = "PTGallery:";

        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".tif", ".tiff", ".svg"
        };

        private static readonly Regex RepeatedWhitespace = new Regex(@"\s+", RegexOptions.Compiled);

        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISiteContextResolver _siteContextResolver;
        private readonly MultiTenantOptions _multiTenantOptions;
        private readonly FolderGalleryOptions _options;
        private readonly IThumbnailStore _thumbnailStore;
        private readonly ILogger<FolderImageEnumerator> _logger;

        public FolderImageEnumerator(
            IWebHostEnvironment environment,
            IHttpContextAccessor httpContextAccessor,
            ISiteContextResolver siteContextResolver,
            IOptions<MultiTenantOptions> multiTenantOptions,
            IOptions<FolderGalleryOptions> options,
            IThumbnailStore thumbnailStore,
            ILogger<FolderImageEnumerator> logger
            )
        {
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
            _siteContextResolver = siteContextResolver;
            _multiTenantOptions = multiTenantOptions.Value;
            _options = options.Value;
            _thumbnailStore = thumbnailStore;
            _logger = logger;
        }

        public async Task<IReadOnlyList<GalleryImage>> GetImagesAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            var images = new List<GalleryImage>();
            var httpContext = _httpContextAccessor.HttpContext;

            _logger.LogDebug(
                "{Prefix} Requested folder path '{FolderPath}'. Host='{Host}' RequestPath='{RequestPath}'",
                LogPrefix, ForLog(folderPath),
                httpContext?.Request.Host.Host ?? "(no http context)",
                httpContext?.Request.Path.Value ?? "(no http context)");

            try
            {
                var location = await GetSiteContentLocationAsync();
                if (location == null)
                {
                    _logger.LogWarning(
                        "{Prefix} No site folder name could be resolved for this request, so no images can be located. " +
                        "The current site context may not be available at render time.",
                        LogPrefix);
                    return images;
                }

                // The folder on disk and the URL prefix are deliberately different
                // things. A root site that serves a related tenant has a blank
                // SiteFolderName, so its files are addressed from the site root
                // ("/site-media/...") even though they live in that tenant's
                // folder on disk ("siteuploadfiles/s1/wwwroot/site-media/...").
                var urlRoot = location.UrlRoot;
                var siteRoot = GetSiteContentRoot(location.SiteFolder);

                _logger.LogDebug(
                    "{Prefix} Site folder on disk='{SiteFolderName}'. Site content root='{SiteRoot}'. URL root='{UrlRoot}'",
                    LogPrefix, location.SiteFolder, siteRoot, urlRoot.Length == 0 ? "(site root)" : urlRoot);

                if (!TryResolvePhysicalFolder(siteRoot, folderPath, out var physicalFolder, out var rejectionReason))
                {
                    _logger.LogWarning(
                        "{Prefix} The folder path '{FolderPath}' was rejected and cannot be read: {Reason}",
                        LogPrefix, ForLog(folderPath), rejectionReason);
                    return images;
                }

                if (!Directory.Exists(physicalFolder))
                {
                    _logger.LogDebug(
                        "{Prefix} No gallery was rendered because the folder does not exist. " +
                        "Looked for '{PhysicalFolder}'. Check both the path spelling and the site folder name '{SiteFolderName}' above.",
                        LogPrefix, physicalFolder, location.SiteFolder);
                    return images;
                }

                var urlFolder = CombineUrlFolder(urlRoot, folderPath);

                // Thumbnails live in a sub-folder beside the originals, so they
                // are reachable by the same static-file pipeline and are never
                // mistaken for gallery images themselves (enumeration below is
                // top-directory-only).
                var thumbnailFolderName = _options.ResolvedFolderName;
                var thumbnailPhysicalFolder = Path.Combine(physicalFolder, thumbnailFolderName);
                var thumbnailUrlFolder = CombineUrlFolder(urlFolder, thumbnailFolderName);

                var allFiles = Directory
                    .EnumerateFiles(physicalFolder, "*", SearchOption.TopDirectoryOnly)
                    .ToList();

                var files = allFiles
                    .Where(f => AllowedExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count == 0)
                {
                    _logger.LogWarning(
                        "{Prefix} The folder '{PhysicalFolder}' contains {FileCount} file(s) but none of them have a recognised " +
                        "image extension (allowed: {Allowed}). No gallery will be rendered.",
                        LogPrefix, physicalFolder, allFiles.Count, string.Join(", ", AllowedExtensions));
                }

                if (allFiles.Count > files.Count)
                {
                    var skipped = allFiles
                        .Where(f => !AllowedExtensions.Contains(Path.GetExtension(f)))
                        .Select(Path.GetFileName)
                        .Take(20);
                    _logger.LogDebug(
                        "{Prefix} Skipped {SkippedCount} non-image file(s) in '{PhysicalFolder}': {Skipped}",
                        LogPrefix, allFiles.Count - files.Count, physicalFolder, string.Join(", ", skipped));
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(fileName)) { continue; }

                    var url = urlFolder + Uri.EscapeDataString(fileName);

                    // Generation is best-effort: any failure leaves the grid
                    // pointing at the original rather than omitting the image.
                    var thumbnailFileName = _thumbnailStore
                        .EnsureThumbnail(file, thumbnailPhysicalFolder, cancellationToken);

                    images.Add(new GalleryImage
                    {
                        FileName = fileName,
                        Url = url,
                        ThumbnailUrl = thumbnailFileName == null
                            ? url
                            : thumbnailUrlFolder + Uri.EscapeDataString(thumbnailFileName),
                        AltText = DeriveAltText(fileName)
                    });
                }

                _logger.LogDebug(
                    "{Prefix} Found {Count} image(s) in '{PhysicalFolder}'. First image URL: {FirstUrl}",
                    LogPrefix, images.Count, physicalFolder,
                    images.Count > 0 ? images[0].Url : "(none)");

                return images;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("{Prefix} Enumeration was cancelled.", LogPrefix);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Prefix} Failed to enumerate images for folder path '{FolderPath}'. The gallery will be omitted.",
                    LogPrefix, ForLog(folderPath));
                return images;
            }
        }

        // The folder a site's files occupy on disk is not always the folder its
        // URLs are prefixed with. A root site that serves a related tenant keeps
        // its files in that tenant's folder ("siteuploadfiles/s1/wwwroot") but
        // is addressed without a prefix ("/site-media/..."), so the two are
        // tracked separately.
        private sealed class SiteContentLocation
        {
            public SiteContentLocation(string siteFolder, string urlRoot)
            {
                SiteFolder = siteFolder;
                UrlRoot = urlRoot;
            }

            public string SiteFolder { get; }

            public string UrlRoot { get; }
        }

        // Keeps an accidentally enormous folder path from flooding the logs.
        private static string ForLog(string value)
        {
            if (value == null) { return "(null)"; }
            const int maxLength = 200;
            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength) + "... (" + value.Length + " chars)";
        }

        private async Task<SiteContentLocation> GetSiteContentLocationAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                _logger.LogWarning(
                    "{Prefix} There is no HttpContext, so the current site cannot be determined. " +
                    "This usually means the gallery is being rendered outside a web request (for example during background processing).",
                    LogPrefix);
                return null;
            }

            ISiteContext siteContext = null;
            try
            {
                siteContext = await _siteContextResolver.ResolveSite(httpContext.Request.Host.Host, httpContext.Request.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Failed to resolve the current site context.", LogPrefix);
                return null;
            }

            if (siteContext == null)
            {
                _logger.LogWarning(
                    "{Prefix} The site context resolver returned null for host '{Host}' and path '{Path}'.",
                    LogPrefix, httpContext.Request.Host.Host, httpContext.Request.Path.Value);
                return null;
            }

            _logger.LogDebug(
                "{Prefix} Current site: Name='{SiteName}' Id='{SiteId}' AliasId='{AliasId}' SiteFolderName='{SiteFolderName}' " +
                "Theme='{Theme}' | options: UseRelatedSitesMode='{UseRelated}' RelatedSiteAliasId='{ConfiguredRelatedAlias}'",
                LogPrefix, siteContext.SiteName, siteContext.Id, ForLog(siteContext.AliasId),
                ForLog(siteContext.SiteFolderName), ForLog(siteContext.Theme),
                _multiTenantOptions.UseRelatedSitesMode, ForLog(_multiTenantOptions.RelatedSiteAliasId));

            // SiteFolderName is the authoritative answer for the folder on disk,
            // but some cloudscribe configurations (notably a parent/root site
            // that serves a related tenant's content) leave it blank and identify
            // the tenant by alias instead. Each candidate is only accepted if its
            // folder really exists on disk, so a wrong guess can never serve
            // another tenant's images by accident.
            //
            // The URL prefix, by contrast, comes only from SiteFolderName, because
            // that is what the site layout uses to build its own asset URLs. A
            // blank SiteFolderName means the site is served from the site root,
            // so its files are addressed as "/site-media/..." and not
            // "/s1/site-media/...".
            var urlRoot = string.IsNullOrWhiteSpace(siteContext.SiteFolderName)
                ? string.Empty
                : "/" + siteContext.SiteFolderName.Trim('/');

            var candidates = new[] { siteContext.SiteFolderName, siteContext.AliasId, _multiTenantOptions.RelatedSiteAliasId };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) { continue; }
                var trimmed = candidate.Trim().Trim('/');
                if (trimmed.Length == 0) { continue; }
                if (!seen.Add(trimmed)) { continue; }

                if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0 || trimmed == "." || trimmed == "..")
                {
                    _logger.LogWarning("{Prefix} Ignoring unusable site folder candidate '{Candidate}'.", LogPrefix, trimmed);
                    continue;
                }

                var candidateRoot = GetSiteContentRoot(trimmed);
                if (Directory.Exists(candidateRoot))
                {
                    _logger.LogDebug(
                        "{Prefix} Using site folder '{SiteFolder}' on disk (content root '{ContentRoot}'). " +
                        "Images will be addressed from URL root '{UrlRoot}'.",
                        LogPrefix, trimmed, candidateRoot, urlRoot.Length == 0 ? "(site root)" : urlRoot);
                    return new SiteContentLocation(trimmed, urlRoot);
                }

                _logger.LogDebug(
                    "{Prefix} Site folder candidate '{Candidate}' has no content folder at '{ContentRoot}', so it was skipped.",
                    LogPrefix, trimmed, candidateRoot);
            }

            _logger.LogWarning(
                "{Prefix} No site folder could be determined for this request, so no images can be located. " +
                "None of the candidate site folders exist under the site's upload root.",
                LogPrefix);
            return null;
        }

        private string GetSiteContentRoot(string siteFolderName)
        {
            var uploadRoot = string.IsNullOrWhiteSpace(_multiTenantOptions.SiteUploadFilesRootFolderName)
                ? "siteuploadfiles"
                : _multiTenantOptions.SiteUploadFilesRootFolderName;

            var contentFolder = string.IsNullOrWhiteSpace(_multiTenantOptions.SiteContentFolderName)
                ? "wwwroot"
                : _multiTenantOptions.SiteContentFolderName;

            return Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                uploadRoot,
                siteFolderName,
                contentFolder));
        }

        private static string CombineUrlFolder(string urlRoot, string folderPath)
        {
            // The root may already end in a separator (a nested folder is built
            // from an existing folder URL), so trailing slashes are trimmed to
            // keep generated URLs single-slashed.
            var root = (urlRoot ?? string.Empty).TrimEnd('/');
            var trimmed = (folderPath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
            if (trimmed.Length == 0) { return root + "/"; }

            var builder = new StringBuilder(root);
            foreach (var segment in trimmed.Split('/'))
            {
                if (segment.Length == 0) { continue; }
                builder.Append('/').Append(Uri.EscapeDataString(segment));
            }
            builder.Append('/');
            return builder.ToString();
        }

        private static bool TryResolvePhysicalFolder(
            string siteRoot,
            string folderPath,
            out string physicalFolder,
            out string reason)
        {
            physicalFolder = null;

            var relative = (folderPath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
            if (relative.Length == 0)
            {
                reason = "the path is empty. Enter a folder path such as /media/gallery.";
                return false;
            }

            // A null byte or other control character makes Path.GetFullPath throw,
            // which would surface as a 500 rather than an empty gallery.
            foreach (var c in relative)
            {
                if (char.IsControl(c))
                {
                    reason = "the path contains a control character or null byte.";
                    return false;
                }
            }

            var segments = relative.Split('/');
            foreach (var segment in segments)
            {
                if (segment.Length == 0)
                {
                    reason = "the path contains an empty segment, for example a doubled slash.";
                    return false;
                }
                if (segment == "." || segment == "..")
                {
                    reason = "the path contains a '.' or '..' segment, which is not allowed.";
                    return false;
                }
            }

            string combined;
            try
            {
                combined = Path.GetFullPath(Path.Combine(siteRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex)
            {
                reason = "the path is not a valid file system path (" + ex.GetType().Name + ").";
                return false;
            }

            // Compare with a trailing separator so a sibling such as
            // "wwwroot-private" can never pass as being inside "wwwroot".
            var siteRootWithSeparator = siteRoot.EndsWith(Path.DirectorySeparatorChar)
                ? siteRoot
                : siteRoot + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(siteRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                reason = "the path resolves outside the site's content root and was blocked.";
                return false;
            }
            if (combined.Length <= siteRootWithSeparator.Length)
            {
                reason = "the path resolves to the site content root itself rather than a folder inside it.";
                return false;
            }

            physicalFolder = combined;
            reason = null;
            return true;
        }

        private static string DeriveAltText(string fileName)
        {
            var withoutExtension = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            var withSpaces = withoutExtension.Replace('-', ' ').Replace('_', ' ');
            return RepeatedWhitespace.Replace(withSpaces, " ").Trim();
        }
    }
}
