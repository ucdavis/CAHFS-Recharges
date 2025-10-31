public class FeedBatch
{
    public Guid BatchID { get; set; }
    public DateTime? DateSent { get; set; }
    public string PickupSentFlag { get; set; } = null!;
    public string AEConsumerReferenceID { get; set; } = null!;
    public string AEConsumnerNotes { get; set; } = null!;
    public string? AERequestStatus { get; set; }
    public string? ErrorDetail { get; set; }
    public Guid? AEConsumerRequestID { get; set; }
    public DateTime? AETransactionDate { get; set; }
    public string? AEJournalName { get; set; }
    public string? AEJournalDescription { get; set; }
    public string? AEJournalReference { get; set; }
    public decimal? BatchTotal { get; set; }
}