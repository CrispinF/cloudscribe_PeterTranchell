using System.ComponentModel.DataAnnotations;

namespace cloudscribe_PeterTranchell_NET6.ViewModels
{
    public class FolderImageGalleryViewModel
    {
        [Display(Name = "Top content (optional)")]
        public string TopContent { get; set; }

        [Display(Name = "Image folder path")]
        [Required(ErrorMessage = "An image folder path is required!")]
        [RegularExpression(@"^/?[A-Za-z0-9][A-Za-z0-9 _.\-/]*$",
            ErrorMessage = "The image folder path must be a relative path from the site root, for example /media/gallery.")]
        [MinLength(1, ErrorMessage = "An image folder path is required!")]
        public string FolderPath { get; set; }

        [Display(Name = "Bottom content (optional)")]
        public string BottomContent { get; set; }
    }
}
