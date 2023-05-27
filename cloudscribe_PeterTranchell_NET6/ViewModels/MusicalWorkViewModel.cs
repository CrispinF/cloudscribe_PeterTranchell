using cloudscribe.SimpleContent.ContentTemplates.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace cloudscribe_PeterTranchell_NET6.ViewModels
{
    public class MusicalWorkViewModel

    {
        //Title
        [Display(Name = "Title")]
        [Required(ErrorMessage = "Title is required!"), MinLength(2, ErrorMessage = "A title cannot be less than 2 characters!")]
        public string WorkTitle { get; set; }

        //Year of composition
        [Display(Name = "Year of composition")]
        [Required(ErrorMessage = "Year is required!")]
        public string WorkYear { get; set; }



        //Genre
        [Display(Name = "Genre")]
        public string Genre { get; set; }

        //Genre pick-list values
        public IEnumerable<SelectListItem> Genres
        {
            get
            {
                return new[]
                {
                new SelectListItem { Value = "Fiction: romance", Text="Fiction: romance" },
                new SelectListItem { Value = "Fiction: horror", Text="Fiction: horror" },
                new SelectListItem { Value = "Fiction: tragedy", Text="Fiction: tragedy" },
                new SelectListItem { Value = "Non-fiction: hobbies and crafts", Text="Non-fiction: hobbies and crafts" },
                new SelectListItem { Value = "Non-fiction: history", Text="Non-fiction: history" },
            };
            }
        }

        //Precis
        [Display(Name = "Precis")]
        [Required(ErrorMessage = "A precis is required!"), MinLength(2, ErrorMessage = "Please tell us a bit more about the work!")]
        public string Precis { get; set; }
    }
}


