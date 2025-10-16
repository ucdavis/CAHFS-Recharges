using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NLog;

namespace CAHFS_Recharges.Pages
{
    public class IndexModel : PageModel
    {
        private readonly Logger _logger;
        private readonly FinancialContext _context;

        public IndexModel(FinancialContext context)
        {
            _context = context;
            _logger = LogManager.GetCurrentClassLogger();
        }

        public void OnGet()
        {
            var user = HttpHelper.HttpContext?.User?.Identity?.Name;
            bool canAccessDatabase = false;
            try
            {
                var feedbatches = _context.FeedBatches.Take(10).ToList();
                canAccessDatabase = true;
            }
            catch (Exception ex)
            {
            }

            ViewData["CanAccessDatabase"] = canAccessDatabase;
            ViewData["User"] = user;
        }
    }
}
