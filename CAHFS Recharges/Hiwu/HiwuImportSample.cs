namespace CAHFS_Recharges.Hiwu
{
    /// Thin search index: one row per sample id in a stored HIWU manifest.
    public class HiwuImportSample
    {
        public Guid FileId { get; set; }

        public string SampleId { get; set; } = "";
    }
}
