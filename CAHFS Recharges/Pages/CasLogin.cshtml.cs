using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net;
using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using NLog;

namespace CAHFS_Recharges.Pages
{
    public class CasLoginModel : PageModel
    {
        private readonly XNamespace _ns = "http://www.yale.edu/tp/cas";
        private const string _strTicket = "ticket";
        private readonly IHttpClientFactory _clientFactory;
        private readonly CasSettings _settings;
        private readonly List<string> _casAttributesToCapture = new() { "authenticationDate", "credentialType" };

        public CasLoginModel(IHttpClientFactory clientFactory, IOptions<CasSettings> settingsOptions)
        {
            _clientFactory = clientFactory;
            _settings = settingsOptions.Value;
        }
        public async Task<IActionResult> OnGet()
        {
            // get ticket & service
            string? ticket = Request.Query[_strTicket];
            string? returnUrl = Request.Query["ReturnUrl"];
            string service = WebUtility.UrlEncode(HttpHelper.GetRootURL() + Request.Path + "?ReturnUrl=" + WebUtility.UrlEncode(returnUrl));
            var client = _clientFactory.CreateClient("CAS");

            try
            {
                var response = await client.GetAsync(_settings.CasBaseUrl + "p3/serviceValidate?ticket=" + ticket + "&service=" + service, HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(responseBody);

                var serviceResponse = doc.Element(_ns + "serviceResponse");
                var successNode = serviceResponse?.Element(_ns + "authenticationSuccess");
                var userNode = successNode?.Element(_ns + "user");
                var validatedUserName = userNode?.Value;

                // uncomment this line temporarily if you ever have issues with users getting unexpected 403(Access Denied) errors in the logs
                // uncommenting this line will log what CAS is sending. When the user in question logs in while trying to access our site
                //HttpHelper.Logger.Log(NLog.LogLevel.Information, "CAS response: " + doc.ToString());

                if (!string.IsNullOrEmpty(validatedUserName))
                {
                    var claimsIdentity = new ClaimsIdentity(
                        [
                        new Claim(ClaimTypes.Name, validatedUserName),
                        new Claim(ClaimTypes.NameIdentifier, validatedUserName),
                        new Claim(ClaimTypes.AuthenticationMethod, "CAS")
                        ],
                        CookieAuthenticationDefaults.AuthenticationScheme);

                    XElement? attributesNode = successNode?.Element(_ns + "attributes");
                    if (attributesNode != null)
                    {
                        foreach (string attributeName in _casAttributesToCapture)
                        {
                            foreach (var element in attributesNode.Elements(_ns + attributeName))
                            {
                                claimsIdentity.AddClaim(new Claim(element.Name.LocalName, element.Value));
                            }
                        }
                    }

                    var user = new ClaimsPrincipal(claimsIdentity);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user);

                    return new LocalRedirectResult(!string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : "/");
                }
            }
            catch (TaskCanceledException ex)
            {// usually caused because the user aborts the page load (HttpContext.RequestAborted)
                LogManager.GetCurrentClassLogger().Info("TaskCanceledException: " + ex.Message.ToString());
            }

            return new ForbidResult();
        }
    }
}
