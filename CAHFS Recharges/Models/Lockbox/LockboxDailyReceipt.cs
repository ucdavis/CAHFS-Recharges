namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxDailyReceipt
    {
        public string LockboxId { get; set; } = null!;
        public DateOnly BusinessDate { get; set; }
        public bool FileReceived { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
    }
}
