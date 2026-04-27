using System;

namespace CAHFS_Recharges.Services
{
    public static class StarLimsDocNumberParser
    {
        public static bool TryParse(string? docNumber, out string folderNo, out int invoiceId, out string? error)
        {
            folderNo = string.Empty;
            invoiceId = default;
            error = null;

            var s = (docNumber ?? string.Empty).Trim();
            if (s.Length < 9)
            {
                error = "DocNumber is too short to parse (expected FolderNo[8] + InvoiceID).";
                return false;
            }

            folderNo = s.Substring(0, 8);
            var invoicePart = s.Substring(8);

            if (!int.TryParse(invoicePart, out invoiceId))
            {
                error = "DocNumber InvoiceID portion is not a valid integer.";
                folderNo = string.Empty;
                invoiceId = default;
                return false;
            }

            return true;
        }
    }
}
