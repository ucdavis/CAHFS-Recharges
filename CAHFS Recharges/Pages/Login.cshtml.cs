using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Net;

namespace CAHFS_Recharges.Pages
{
    /// Branded CAEI login entry. CAS redirect runs only via the Cas handler.
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly CasSettings _settings;

        public LoginModel(IOptions<CasSettings> settingsOptions)
        {
            _settings = settingsOptions.Value;
        }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool SignedOut { get; set; }

        public IActionResult OnGet()
        {
            if (User.Identity?.IsAuthenticated == true)
                return LocalRedirect(GetSafeAppReturnUrl());

            if (string.IsNullOrEmpty(ReturnUrl))
                ReturnUrl = "/Home";

            return Page();
        }

        /// Starts the UC Davis CAS login redirect (same service URL construction as before).
        public IActionResult OnGetCas()
        {
            if (User.Identity?.IsAuthenticated == true)
                return LocalRedirect(GetSafeAppReturnUrl());

            Uri url = new(Request.GetDisplayUrl());
            string baseURl = url.GetLeftPart(UriPartial.Authority);
            string returnURL = HttpHelper.GetRootURL().Replace(baseURl, "").Replace("Login", "", StringComparison.CurrentCultureIgnoreCase);

            if (!string.IsNullOrEmpty(Request.Query["ReturnUrl"]))
            {
                returnURL = Request.Query["ReturnUrl"].ToString();
            }
            else if (!string.IsNullOrEmpty(ReturnUrl))
            {
                returnURL = ReturnUrl;
            }

            var forceRenew = SignedOut
                || Request.Cookies.ContainsKey(LogoutModel.RequireCasRenewCookieName);

            if (Request.Cookies.ContainsKey(LogoutModel.RequireCasRenewCookieName))
            {
                Response.Cookies.Delete(
                    LogoutModel.RequireCasRenewCookieName,
                    new CookieOptions { Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
            }

            var redirectUrl = HttpHelper.GetRootURL() + new PathString("/CasLogin");
            var authorizationEndpoint = (_settings.CasBaseUrl ?? string.Empty).TrimEnd('/') + "/login?service=" +
                WebUtility.UrlEncode(redirectUrl + "?ReturnUrl=" + WebUtility.UrlEncode(returnURL));

            // After logout, require primary credentials even if the campus CAS TGC is still present.
            if (forceRenew)
                authorizationEndpoint += "&renew=true";

            return new RedirectResult(authorizationEndpoint);
        }

        private string GetSafeAppReturnUrl()
        {
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return ReturnUrl;

            if (!string.IsNullOrEmpty(Request.Query["ReturnUrl"]) && Url.IsLocalUrl(Request.Query["ReturnUrl"]))
                return Request.Query["ReturnUrl"].ToString();

            var pathBase = HttpContext.Request.PathBase.Value ?? string.Empty;
            return string.IsNullOrEmpty(pathBase) ? "/Home" : pathBase + "/Home";
        }
    }
}
