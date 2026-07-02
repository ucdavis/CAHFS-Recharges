namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxPaymentStagingRow
    {
        public string LockboxNumber { get; set; } = "";
        public DateTime DepositDate { get; set; }
        public string BatchNumber { get; set; } = "";
        public int SeqNumber { get; set; }
        public string BankNumber { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string CheckNumber { get; set; } = "";
        public string RemitterName { get; set; } = "";
        public decimal CheckAmount { get; set; }
        public string? BillingId { get; set; }
        public string? AccessionFull { get; set; }
        public decimal? InvAmt { get; set; }
        public string ValidationStatus { get; set; } = "";
        public string? ValidationNotes { get; set; }
    }
}
