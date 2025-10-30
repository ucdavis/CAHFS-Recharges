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
        private readonly StarLIMSContext _slimsContext;

        public IndexModel(FinancialContext context, StarLIMSContext slimsContext)
        {
            _context = context;
            _logger = LogManager.GetCurrentClassLogger();
            _slimsContext = slimsContext;
        }

        public void OnGet()
        {
            var user = HttpHelper.HttpContext?.User?.Identity?.Name;
            var settings = HttpHelper.Settings;
            bool canAccessDatabase = false;
            ViewData["ErrorMessage"] = "";

            try
            {
                //var feedbatches = _context.FeedBatches.Take(10).ToList();
                var ordtasks = _slimsContext.OrdTasks.Take(10).ToList();
                canAccessDatabase = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error accessing database");
                _logger.Error(ex, ex.Message);

                ViewData["ErrorMessage"] = ex.Message;
            }

            ViewData["CanAccessDatabase"] = canAccessDatabase;
            ViewData["User"] = user;
        }
    }
}
