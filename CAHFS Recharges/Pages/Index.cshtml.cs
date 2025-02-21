using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NLog;

namespace CAHFS_Recharges.Pages
{
    public class IndexModel : PageModel
    {
        private readonly Logger _logger;

        public IndexModel()
        {
            _logger = LogManager.GetCurrentClassLogger();
        }

        public void OnGet()
        {
            var user = HttpHelper.HttpContext?.User?.Identity?.Name;
            ViewData["User"] = user;
        }
    }
}
