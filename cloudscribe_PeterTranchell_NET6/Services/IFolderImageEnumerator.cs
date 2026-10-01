using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace cloudscribe_PeterTranchell_NET6.Services
{
    public class GalleryImage
    {
        public string FileName { get; set; }

        /// <summary>URL of the original image, used by the full-size viewer.</summary>
        public string Url { get; set; }

        /// <summary>
        /// URL of the small image used by the grid. Falls back to
        /// <see cref="Url"/> when a thumbnail is unavailable, so the grid still
        /// renders if generation is turned off or has failed for this image.
        /// </summary>
        public string ThumbnailUrl { get; set; }

        public string AltText { get; set; }
    }

    public interface IFolderImageEnumerator
    {
        Task<IReadOnlyList<GalleryImage>> GetImagesAsync(string folderPath, CancellationToken cancellationToken = default);
    }
}
