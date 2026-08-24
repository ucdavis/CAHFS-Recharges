using System.Collections.Generic;

namespace LockboxCashReceiptPoster.Options
{
    public sealed class CashReceiptPosterOptions
    {
        public const string SectionName = "CashReceiptPoster";

        public bool Enabled { get; set; } = true;
        public string CheckbookId { get; set; } = "CASH RECEIPTS";
        ///RunDate = today (DOCDATE/GLPOSTDT). DepositDate = bank deposit date.
        public CashReceiptDateSource DateSource { get; set; } = CashReceiptDateSource.RunDate;
        public string LogDirectory { get; set; } = "logs";
        /// Optional default row cap (0 = no cap). CLI --max overrides.
        public int MaxRows { get; set; }
        /// Full path to Microsoft.Dynamics.GP.eConnect.dll (loaded at runtime).
        public string EConnectDllPath { get; set; } =
            @"C:\Program Files (x86)\Microsoft Dynamics\eConnect 18.0\API\Microsoft.Dynamics.GP.eConnect.dll";
        public EmailOptions Email { get; set; } = new EmailOptions();
        public Dictionary<string, CompanyOptions> Companies { get; set; } =
            new Dictionary<string, CompanyOptions>(System.StringComparer.OrdinalIgnoreCase);
    }

    public enum CashReceiptDateSource
    {
        DepositDate,
        RunDate
    }

    public sealed class CompanyOptions
    {
        public string ConnectionStringName { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public sealed class EmailOptions
    {
        public bool Enabled { get; set; }
        public string SmtpHost { get; set; } = "smtp.ucdavis.edu";
        public int SmtpPort { get; set; } = 25;
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }
}
