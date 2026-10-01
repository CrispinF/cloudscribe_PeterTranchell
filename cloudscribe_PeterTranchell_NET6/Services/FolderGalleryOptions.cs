using System;

namespace cloudscribe_PeterTranchell_NET6.Services
{
    /// <summary>
    /// Tuning for the folder-based image gallery. Bound from the "FolderGallery"
    /// configuration section; every value has a sensible default so the section
    /// may be omitted entirely.
    /// </summary>
    public class FolderGalleryOptions
    {
        public const string SectionName = "FolderGallery";

        /// <summary>
        /// When false the grid is rendered from the original images and no
        /// thumbnails are generated. Useful for turning the feature off without
        /// a code change.
        /// </summary>
        public bool EnableThumbnails { get; set; } = true;

        /// <summary>
        /// Longest edge of a generated thumbnail, in pixels. The other edge is
        /// scaled proportionally, so an image is never stretched.
        /// </summary>
        public int ThumbnailMaxDimension { get; set; } = 320;

        /// <summary>
        /// JPEG quality for generated thumbnails, 1-100.
        /// </summary>
        public int ThumbnailQuality { get; set; } = 82;

        /// <summary>
        /// Name of the sub-folder that holds generated thumbnails, created on
        /// demand beneath the gallery folder.
        /// </summary>
        public string ThumbnailFolderName { get; set; } = "thumbnails";

        /// <summary>
        /// Upper bound on how many missing thumbnails a single request will
        /// generate. A very large gallery therefore fills in across successive
        /// requests instead of risking a request timeout on the first hit.
        /// </summary>
        public int MaxThumbnailsPerRequest { get; set; } = 400;

        /// <summary>
        /// Clamps configured values into safe ranges so a typo in configuration
        /// cannot produce corrupt or enormous thumbnails.
        /// </summary>
        public int ResolvedMaxDimension =>
            Math.Clamp(ThumbnailMaxDimension, 32, 4096);

        public int ResolvedQuality =>
            Math.Clamp(ThumbnailQuality, 1, 100);

        public int ResolvedMaxPerRequest =>
            Math.Clamp(MaxThumbnailsPerRequest, 1, 20000);

        public string ResolvedFolderName =>
            string.IsNullOrWhiteSpace(ThumbnailFolderName)
                ? "thumbnails"
                : ThumbnailFolderName.Trim();
    }
}
