namespace CAHFS_Recharges.Models.Lockbox
{
    public class LockboxAlertState
    {
        public string LockboxId { get; set; } = null!;
        public DateTime? LastAlertSentUtc { get; set; }
        public int LastConsecutiveMissingDays { get; set; }
    }
}
