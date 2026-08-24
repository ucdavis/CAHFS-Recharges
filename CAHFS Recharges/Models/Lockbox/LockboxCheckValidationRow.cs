namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxCheckValidationRow
    {
        public int StagingId { get; set; }
        public DateTime DepositDate { get; set; }
        public string CheckNumber { get; set; } = "";
        public decimal CheckAmount { get; set; }
        public string? BillingId { get; set; }
        public string ValidationStatus { get; set; } = "";
        public string? ValidationNotes { get; set; }
        public string FileName { get; set; } = "";
        public string? PostStatus { get; set; }
        public string BatchNumber { get; set; } = "";
        public string? RemitterName { get; set; }
        public string? AccessionFull { get; set; }

        /// Plain-language reason the row is blocked (for Check Validations UI)
        public string IssueSummary
        {
            get
            {
                var notes = (ValidationNotes ?? "").Trim();
                var notesLower = notes.ToLowerInvariant();

                return ValidationStatus switch
                {
                    "INVALID_CUSTOMER" when notesLower.Contains("blank") || notesLower.Contains("cleared")
                        => "Billing ID is missing",
                    "INVALID_CUSTOMER" when notesLower.Contains("not found") || notesLower.Contains("rm00101")
                        => "Billing ID was not found in Great Plains",
                    "INVALID_CUSTOMER"
                        => "Billing ID needs correction",
                    "WARN" when notesLower.Contains("no addenda") || notesLower.Contains("missing addenda")
                        => "Check has no payment details from the bank file",
                    "WARN"
                        => "Billing ID may need to be entered manually",
                    "ERROR" when notesLower.Contains("trailer") || notesLower.Contains("total")
                        => "Bank file totals do not match the detail lines",
                    "ERROR"
                        => "This payment failed file validation",
                    _ when !string.IsNullOrWhiteSpace(notes)
                        => TruncateForDisplay(CleanNotes(notes), 100),
                    _ => "Needs review"
                };
            }
        }

        ///Short next step for the operator (shown under IssueSummary).
        public string IssueAction => ValidationStatus switch
        {
            "INVALID_CUSTOMER" => "Enter the correct GP customer number and Save.",
            "WARN" => "Enter the Billing ID if known, then Save.",
            "ERROR" => "Review staging for this deposit or reprocess the lockbox file.",
            _ => ""
        };

        public string StatusDisplayName => ValidationStatus switch
        {
            "INVALID_CUSTOMER" => "Invalid customer",
            "WARN" => "Needs Billing ID",
            "ERROR" => "File error",
            "VALID" => "Valid",
            "PENDING" => "Pending",
            _ => ValidationStatus
        };

        private static string CleanNotes(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return "";

            return notes
                .Replace(" after trim/zero-pad", "", StringComparison.OrdinalIgnoreCase)
                .Replace("RM00101", "Great Plains", StringComparison.OrdinalIgnoreCase)
                .Replace("CUSTNMBR", "customer number", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        private static string TruncateForDisplay(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
                return text;
            return text.Substring(0, maxLen).TrimEnd() + "…";
        }
    }

    public class LockboxCheckValidationSummary
    {
        public int InvalidCustomerCount { get; set; }
        public int WarnCount { get; set; }
        public int ErrorCount { get; set; }
    }

    public sealed class LockboxBillingIdUpdateResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public bool BecameValid { get; init; }

        public static LockboxBillingIdUpdateResult Ok(string message, bool becameValid) =>
            new() { Success = true, Message = message, BecameValid = becameValid };

        public static LockboxBillingIdUpdateResult Fail(string message) =>
            new() { Success = false, Message = message };
    }
}
