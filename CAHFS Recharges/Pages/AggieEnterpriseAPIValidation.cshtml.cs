using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ZeroQL.Client;

namespace CAHFS_Recharges.Pages
{
    public class AggieEnterpriseAPIValidationModel : PageModel
    {
        public async void OnGet()
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(HttpHelper.GetSetting<string>("AggieEnterprise", "BaseUrl") ?? "");
            var AeClient = new AggieEnterpriseAPIClient(httpClient);

            var response = await AeClient.Query(o => o.ErpApiInfo(o => new { o.VersionNumber, o.ApiSchema, o.CommittedOn }));
        }
    }
}
