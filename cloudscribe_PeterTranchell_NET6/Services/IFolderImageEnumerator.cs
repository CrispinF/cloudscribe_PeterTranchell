using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace cloudscribe_PeterTranchell_NET6.Services
{
    public class GalleryImage
    {
        public string FileName { get; set; }
        public string Url { get; set; }
        public string AltText { get; set; }
    }

    public interface IFolderImageEnumerator
    {
        Task<IReadOnlyList<GalleryImage>> GetImagesAsync(string folderPath, CancellationToken cancellationToken = default);
    }
}
