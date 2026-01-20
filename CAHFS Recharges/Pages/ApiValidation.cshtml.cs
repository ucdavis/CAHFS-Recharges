using System.Linq;
using System.Threading.Tasks;
using CAHFS.GraphQl;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections;
using System.Text;
using System;

namespace CAHFS_Recharges.Pages
{
    public class ApiValidationModel : PageModel
    {
        private readonly IAggieEnterpriseClient _aggieEnterpriseClient;

        public ApiValidationModel(IAggieEnterpriseClient aggieEnterpriseClient)
        {
            _aggieEnterpriseClient = aggieEnterpriseClient;
        }

        // /ApiValidation?Chart1=...&Chart2=...
        [BindProperty(SupportsGet = true)]
        public string? Chart1 { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Chart2 { get; set; }

        private static string Normalize(string? s)
            => (s ?? "").Trim();

        // Print the full Extensions.response (status + body)
        static string DumpObj(object? obj, int depth = 0)
        {
            if (obj == null) return "null";
            if (depth > 6) return obj.ToString() ?? "";

            if (obj is IDictionary dict)
            {
                var sb = new StringBuilder();
                foreach (DictionaryEntry kv in dict)
                {
                    sb.Append($"{kv.Key}=");
                    sb.Append(DumpObj(kv.Value, depth + 1));
                    sb.Append("; ");
                }
                return sb.ToString();
            }

            if (obj is IEnumerable en && obj is not string)
            {
                var sb = new StringBuilder();
                foreach (var x in en)
                {
                    sb.Append(DumpObj(x, depth + 1));
                    sb.Append(", ");
                }
                return sb.ToString();
            }

            return obj.ToString() ?? "";
        }

        static string DumpExtensions(IReadOnlyDictionary<string, object?>? ext)
        {
            if (ext == null || ext.Count == 0) return "";
            return string.Join(" | ", ext.Select(kv => $"{kv.Key}={DumpObj(kv.Value)}"));
        }

        public async Task OnGetAsync()
        {
            // --- API Info
            var erpApiInfoOp = await _aggieEnterpriseClient.ErpApiInfo.ExecuteAsync();
            ViewData["ErpApiInfo"] = erpApiInfoOp.Data;
            ViewData["ErpApiInfoErrors"] = erpApiInfoOp.Errors?
                .Select(e => $"{e.Message} | Ext={DumpExtensions(e.Extensions)}")
                .ToList();

            // --- Chart 1 (ERP VALIDATION)
            if (!string.IsNullOrWhiteSpace(Chart1))
            {
                var input1 = Normalize(Chart1);

                var op1 = await _aggieEnterpriseClient.ErpValidateChartstring.ExecuteAsync(
                    segmentString: input1,
                    validateCVRs: true,
                    accountingDate: null
                );

                ViewData["ErpChartValidation1"] = op1.Data; // .Data
                ViewData["ErpChartValidation1Input"] = input1;
                ViewData["ErpChartValidation1Errors"] = op1.Errors?
                    .Select(e => $"{e.Message} | Ext={DumpExtensions(e.Extensions)}")
                    .ToList();
            }

            // --- Chart 2 (ERP VALIDATION)
            if (!string.IsNullOrWhiteSpace(Chart2))
            {
                var input2 = Normalize(Chart2);

                var op2 = await _aggieEnterpriseClient.ErpValidateChartstring.ExecuteAsync(
                    segmentString: input2,
                    validateCVRs: true,
                    accountingDate: null
                );

                ViewData["ErpChartValidation2"] = op2.Data; // .Data
                ViewData["ErpChartValidation2Input"] = input2;
                ViewData["ErpChartValidation2Errors"] = op2.Errors?
                    .Select(e => $"{e.Message} | Ext={DumpExtensions(e.Extensions)}")
                    .ToList();
            }
        }
    }
}
