using CAHFS.GraphQl;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    public class ApiValidationModel : PageModel
    {
        private readonly IAggieEnterpriseClient _aggieEnterpriseClient;
        public ApiValidationModel(IAggieEnterpriseClient aggieEnterpriseClient)
        {
            _aggieEnterpriseClient = aggieEnterpriseClient;
        }

        public async Task OnGetAsync()
        {
            var chart1 = await _aggieEnterpriseClient.GlValidateChartstring.ExecuteAsync("3110-14107-VMDO143-522201-43-000-0000000000-200507-0000-000000-000000", true);
            ViewData["ChartStringValidation1"] = chart1.Data;

            var chart2 = await _aggieEnterpriseClient.GlValidateChartstring.ExecuteAsync("3110-14107-XXXX000-522201-43-000-0000000000-200507-0000-000000-000000", true);
            ViewData["ChartStringValidation2"] = chart2.Data;

            //var erpApiInfo = await _eggie
            var erpApiInfo = await _aggieEnterpriseClient.ErpApiInfo.ExecuteAsync();
            ViewData["ErpApiInfo"] = erpApiInfo.Data;
        }
    }
}
