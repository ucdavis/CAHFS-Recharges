using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    public class DeniedModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? Reason { get; set; }

        public string Title { get; set; } = "Access Denied";
        public string Message { get; set; } = "You do not have permission to access this resource.";
        public string Details { get; set; } = "";

        public void OnGet()
        {
            switch (Reason?.ToLowerInvariant())
            {
                case "hangfire":
                    Title = "Admin Access Required";
                    Message = "The Hangfire Dashboard is restricted to administrators only.";
                    Details = "If you need access to view or manage background jobs, please contact your system administrator to be added to the Admin role.";
                    break;

                case "operator":
                    Title = "Operator Access Required";
                    Message = "This action requires Operator permissions.";
                    Details = "Operators can validate COA data and send batches to Aggie Enterprise. Contact your administrator for access.";
                    break;

                default:
                    Title = "Access Denied";
                    Message = "You do not have permission to access this resource.";
                    Details = "If you believe this is an error, please contact your system administrator.";
                    break;
            }
        }
    }
}
