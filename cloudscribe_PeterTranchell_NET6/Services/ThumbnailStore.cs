using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using ImageColor = SixLabors.ImageSharp.Color;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace cloudscribe_PeterTranchell_NET6.Services
{
    public interface IThumbnailStore
    {
        /// <summary>
        /// Ensures a thumbnail exists for <paramref name="imagePath"/> inside
        /// <paramref name="thumbnailFolder"/>, generating it if missing or stale.
        /// Returns the thumbnail file name, or null when one cannot be produced
        /// (unsupported format, unreadable source, generation disabled), in which
        /// case the caller should fall back to the original image.
        /// </summary>
        string EnsureThumbnail(string imagePath, string thumbnailFolder, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Generates and caches downscaled copies of gallery images.
    /// </summary>
    /// <remarks>
    /// Generation is deliberately lazy and cheap when the cache is warm: a warm
    /// hit costs one <see cref="File.Exists"/> and one timestamp read per image.
    /// A per-folder lock means concurrent first requests generate each thumbnail
    /// once rather than racing, and a per-request budget stops a very large folder
    /// from pushing a single request past its timeout.
    /// </remarks>
    public sealed class ThumbnailStore : IThumbnailStore, IDisposable
    {
        private const string LogPrefix = "PTGallery:";

        /// <summary>
        /// Vector and animated formats are served as-is. Rescaling them would
        /// rasterise a scalable asset or drop all but the first animation frame.
        /// </summary>
        private static readonly HashSet<string> PassthroughExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".svg", ".gif", ".avif"
            };

        private static readonly Dictionary<string, SemaphoreSlim> FolderLocks =
            new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private readonly FolderGalleryOptions _options;
        private readonly ILogger<ThumbnailStore> _logger;
        private readonly SemaphoreSlim _budget = new SemaphoreSlim(1, 1);
        private int _generatedThisRequest;

        public ThumbnailStore(
            IOptions<FolderGalleryOptions> options,
            ILogger<ThumbnailStore> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public string EnsureThumbnail(string imagePath, string thumbnailFolder, CancellationToken cancellationToken = default)
        {
            if (!_options.EnableThumbnails || string.IsNullOrEmpty(imagePath) || string.IsNullOrEmpty(thumbnailFolder))
            {
                return null;
            }

            var fileName = Path.GetFileName(imagePath);
            if (string.IsNullOrEmpty(fileName)) { return null; }

            var extension = Path.GetExtension(fileName);
            if (PassthroughExtensions.Contains(extension))
            {
                return null;
            }

            // The full original filename is kept in the thumbnail name so the
            // mapping stays reversible and, more importantly, so "photo.jpg" and
            // "photo.png" in the same folder cannot overwrite each other's
            // cached copy.
            var thumbnailName = fileName + ".thumb.jpg";
            var thumbnailPath = Path.Combine(thumbnailFolder, thumbnailName);

            if (IsUsable(imagePath, thumbnailPath)) { return thumbnailName; }

            return Generate(imagePath, thumbnailPath, thumbnailFolder, thumbnailName, fileName, cancellationToken);
        }

        /// <summary>
        /// True when a cached thumbnail exists and is at least as new as its source.
        /// </summary>
        private static bool IsUsable(string imagePath, string thumbnailPath)
        {
            try
            {
                var source = new FileInfo(imagePath);
                var cached = new FileInfo(thumbnailPath);

                if (!source.Exists || !cached.Exists) { return false; }
                if (cached.Length == 0) { return false; }

                // A source newer than its thumbnail means the original was
                // replaced, so the cached copy is stale.
                return cached.LastWriteTimeUtc >= source.LastWriteTimeUtc;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string Generate(
            string imagePath,
            string thumbnailPath,
            string thumbnailFolder,
            string thumbnailName,
            string originalName,
            CancellationToken cancellationToken)
        {
            if (!TryReserveBudget()) { return null; }

            var gate = GetFolderLock(thumbnailFolder);
            if (!gate.Wait(0))
            {
                // Another request is already generating for this folder. Fall back
                // to the original rather than blocking the page render.
                return null;
            }

            try
            {
                // Re-check inside the lock: the winner may have just written it.
                if (IsUsable(imagePath, thumbnailPath)) { return thumbnailName; }

                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(thumbnailFolder);

                var maxDimension = _options.ResolvedMaxDimension;

                using (var image = Image.Load(imagePath))
                {
                    // Honour the EXIF orientation flag before measuring, otherwise
                    // a rotated photo is scaled against the wrong dimensions.
                    image.Mutate(x => x.AutoOrient());

                    var longestEdge = Math.Max(image.Width, image.Height);
                    if (longestEdge > maxDimension)
                    {
                        var scale = (double)maxDimension / longestEdge;
                        var targetWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                        var targetHeight = Math.Max(1, (int)Math.Round(image.Height * scale));

                        image.Mutate(x => x.Resize(targetWidth, targetHeight, KnownResamplers.Lanczos3));
                    }

                    // Write to a temporary file and move into place, so a failed
                    // or cancelled write never leaves a truncated thumbnail that
                    // later looks like a valid cache hit.
                    var tempPath = thumbnailPath + ".tmp-" + Guid.NewGuid().ToString("N") + ".jpg";

                    try
                    {
                        SaveAsJpeg(image, tempPath);
                        File.Move(tempPath, thumbnailPath, overwrite: true);
                    }
                    finally
                    {
                        TryDelete(tempPath);
                    }
                }

                _logger.LogDebug(
                    "{Prefix} Generated thumbnail '{Thumbnail}' for '{Original}'.",
                    LogPrefix, thumbnailName, originalName);

                return thumbnailName;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug(
                    "{Prefix} Thumbnail generation for '{Original}' was cancelled.", LogPrefix, originalName);
                throw;
            }
            catch (Exception ex)
            {
                // One unreadable or corrupt file must not fail the whole gallery.
                _logger.LogWarning(
                    ex,
                    "{Prefix} Could not generate a thumbnail for '{Original}'; the grid will use the original image. {Message}",
                    LogPrefix, originalName, ex.Message);
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private void SaveAsJpeg(Image image, string path)
        {
            // JPEG has no alpha channel, so any transparency in a PNG source
            // would otherwise composite onto black. Matting onto white is
            // applied unconditionally rather than probing the pixel format:
            // decoded-format alpha detection proved unreliable across formats,
            // and the cost of the extra pass is negligible next to encoding.
            image.Mutate(x => x.BackgroundColor(ImageColor.White));

            image.Save(path, new JpegEncoder { Quality = _options.ResolvedQuality });
        }

        private bool TryReserveBudget()
        {
            if (_generatedThisRequest >= _options.ResolvedMaxPerRequest) { return false; }

            _budget.Wait(0);
            try
            {
                if (_generatedThisRequest >= _options.ResolvedMaxPerRequest) { return false; }
                _generatedThisRequest++;
                return true;
            }
            finally
            {
                _budget.Release();
            }
        }

        private static SemaphoreSlim GetFolderLock(string folder)
        {
            lock (FolderLocks)
            {
                if (!FolderLocks.TryGetValue(folder, out var gate))
                {
                    gate = new SemaphoreSlim(1, 1);
                    FolderLocks.Add(folder, gate);
                }
                return gate;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
            catch (Exception)
            {
                // Leaving a stray temp file behind is preferable to masking the
                // original failure.
            }
        }

        public void Dispose()
        {
            _budget.Dispose();
        }
    }
}
