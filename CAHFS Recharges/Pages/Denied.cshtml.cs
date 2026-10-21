using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    /// Legacy route; redirects to /AccessDenied for a single branded deny experience.
    [AllowAnonymous]
    public class DeniedModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? Reason { get; set; }

        public IActionResult OnGet()
        {
            return RedirectToPage("/AccessDenied", new { reason = Reason });
        }
    }
}
