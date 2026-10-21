using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    [AllowAnonymous]
    public class AccessDeniedModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? Reason { get; set; }

        public string Title { get; private set; } = "Access Denied";
        public string Message { get; private set; } = "You do not have permission to access this resource.";
        public string Details { get; private set; } = string.Empty;

        public void OnGet()
        {
            switch (Reason?.Trim().ToLowerInvariant())
            {
                case "app":
                    Title = "Not Provisioned for CAEI";
                    Message = "You signed in with university CAS, but your Kerberos ID is not on the CAEI allowlist.";
                    Details = "Contact your system administrator to be added to Admins, Operators, or Viewers in the authorization configuration.";
                    break;

                case "workspace":
                    Title = "Workspace Access Required";
                    Message = "You do not have access to this integration workspace.";
                    Details = "Aggie Enterprise, Lockbox, and HIWU are separate. Ask an administrator to grant the product family and lab you need.";
                    break;

                case "capability":
                    Title = "Action Not Permitted";
                    Message = "Your role does not allow this action.";
                    Details = "Viewers can read and filter. Operators and Admins can mutate and send. Contact your administrator if you need a higher role.";
                    break;

                case "hangfire":
                    Title = "Admin Access Required";
                    Message = "The Hangfire Dashboard is restricted to administrators only.";
                    Details = "Contact your system administrator to be added to the Admin role.";
                    break;

                case "operator":
                    Title = "Operator Access Required";
                    Message = "This action requires Operator permissions.";
                    Details = "Operators can validate COA data and send batches to Aggie Enterprise. Contact your administrator for access.";
                    break;

                default:
                    Title = "Access Denied";
                    Message = "You do not have permission to access this resource.";
                    Details = "If you believe this is an error, contact your system administrator.";
                    break;
            }
        }
    }
}
