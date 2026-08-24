namespace LockboxCashReceiptPoster.Models
{
    /// Destination-agnostic cash receipt request (IM field contract).
    /// Same DTO is used for GP eConnect today and TraceFirst later.
    public sealed class CashReceiptRequest
    {
        public int StagingId { get; set; }
        public string FileName { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string CheckNumber { get; set; } = "";
        public decimal Amount { get; set; }
        public System.DateTime DepositDate { get; set; }
        public string BatchNumber { get; set; } = "";
    }

    public sealed class PostResult
    {
        public bool Success { get; set; }
        public string? DocumentNumber { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public static PostResult Ok(string? documentNumber = null) =>
            new PostResult { Success = true, DocumentNumber = documentNumber };

        public static PostResult Fail(string message, string? code = null) =>
            new PostResult { Success = false, ErrorMessage = message, ErrorCode = code };
    }
}
