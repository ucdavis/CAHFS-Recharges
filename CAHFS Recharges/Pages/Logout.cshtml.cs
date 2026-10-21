using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    /// Clears the CAEI auth session and workspace cookies, then returns to Login.
    /// Sets a short-lived marker so the next CAS login uses renew=true (campus SSO cookie
    /// cannot be cleared from a cross-site iframe; top-level CAS /logout is required for that).
    [AllowAnonymous]
    public class LogoutModel : PageModel
    {
        public const string RequireCasRenewCookieName = "CAEI.RequireCasRenew";

        private readonly IIntegrationContextService _integrationContext;

        public LogoutModel(IIntegrationContextService integrationContext)
        {
            _integrationContext = integrationContext;
        }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Belt-and-suspenders: expire the named auth cookie even if SignOut missed a path match.
            Response.Cookies.Delete(
                "CAHFSRecharge.Authentication.UCD",
                new CookieOptions { Secure = true, SameSite = SameSiteMode.Lax, Path = "/" });

            _integrationContext.ClearIntegrationCookie(HttpContext);
            _integrationContext.ClearProductFamilyCookie(HttpContext);

            Response.Cookies.Append(
                RequireCasRenewCookieName,
                "1",
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromMinutes(15),
                    IsEssential = true,
                    Path = "/"
                });

            return RedirectToPage("/Login", new { SignedOut = true, ReturnUrl = GetSafeReturnUrl() });
        }

        private string GetSafeReturnUrl()
        {
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return ReturnUrl;

            if (!string.IsNullOrEmpty(Request.Query["ReturnUrl"]) && Url.IsLocalUrl(Request.Query["ReturnUrl"]))
                return Request.Query["ReturnUrl"].ToString();

            return "/Home";
        }
    }
}
