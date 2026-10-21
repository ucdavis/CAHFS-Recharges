using CAHFS_Recharges.Models;
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

            var cookiePath = HttpHelper.CookiePath(Request);
            var secure = Request.IsHttps;

            // Belt-and-suspenders: expire the named auth cookie even if SignOut missed a path match.
            // Under PathBase the auth cookie is scoped to /caei-test; also clear Path="/" for older cookies.
            Response.Cookies.Delete(
                "CAHFSRecharge.Authentication.UCD",
                new CookieOptions { Secure = secure, SameSite = SameSiteMode.Lax, Path = cookiePath });
            if (cookiePath != "/")
            {
                Response.Cookies.Delete(
                    "CAHFSRecharge.Authentication.UCD",
                    new CookieOptions { Secure = secure, SameSite = SameSiteMode.Lax, Path = "/" });
            }

            _integrationContext.ClearIntegrationCookie(HttpContext);
            _integrationContext.ClearProductFamilyCookie(HttpContext);

            Response.Cookies.Append(
                RequireCasRenewCookieName,
                "1",
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = secure,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromMinutes(15),
                    IsEssential = true,
                    Path = cookiePath
                });

            return RedirectToPage("/Login", new { SignedOut = true, ReturnUrl = GetSafeReturnUrl() });
        }

        private string GetSafeReturnUrl()
        {
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return HttpHelper.WithPathBase(Request, ReturnUrl);

            if (!string.IsNullOrEmpty(Request.Query["ReturnUrl"]) && Url.IsLocalUrl(Request.Query["ReturnUrl"]))
                return HttpHelper.WithPathBase(Request, Request.Query["ReturnUrl"].ToString());

            return HttpHelper.WithPathBase(Request, "/Home");
        }
    }
}
