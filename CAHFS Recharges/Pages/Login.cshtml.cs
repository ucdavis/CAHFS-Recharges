using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Net;
using System.Runtime;

namespace CAHFS_Recharges.Pages
{
    public class LoginModel : PageModel
    {
        private readonly CasSettings _settings;
        public LoginModel(IOptions<CasSettings> settingsOptions) : base()
        {
            _settings = settingsOptions.Value;
        }

        public IActionResult OnGet()
        {
            Uri url = new(Request.GetDisplayUrl());
            string baseURl = url.GetLeftPart(UriPartial.Authority);
            string returnURL = HttpHelper.GetRootURL().Replace(baseURl, "").Replace("Login", "", StringComparison.CurrentCultureIgnoreCase);

            if (!string.IsNullOrEmpty(Request.Query["ReturnUrl"]))
            {
                returnURL = Request.Query["ReturnUrl"].ToString();
            }

            var redirectUrl = HttpHelper.GetRootURL() + new PathString("/CasLogin");
            var authorizationEndpoint = _settings.CasBaseUrl + "login?service=" + 
                WebUtility.UrlEncode(redirectUrl + "?ReturnUrl=" + WebUtility.UrlEncode(returnURL));


            return new RedirectResult(authorizationEndpoint);
        }
    }
}
