namespace CAHFS_Recharges.Hiwu
{
    public class HiwuSampleReview
    {
        public Guid FileId { get; set; }

        public string SampleKey { get; set; } = "";

        public string ReviewedBy { get; set; } = "";

        public DateTime ReviewedUtc { get; set; }
    }
}
