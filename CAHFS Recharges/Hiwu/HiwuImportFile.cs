namespace CAHFS_Recharges.Hiwu
{
    public class HiwuImportFile
    {
        public Guid FileId { get; set; }

        public string RemoteFileName { get; set; } = "";

        public DateTime ReceivedUtc { get; set; }

        public string RawXml { get; set; } = "";

        public long SizeBytes { get; set; }

        public string ContentHash { get; set; } = "";

        public string ParseStatus { get; set; } = HiwuParseStatus.Stored;

        public string? ParseError { get; set; }

        public bool IsAmendment { get; set; }

        public Guid? OriginalFileId { get; set; }

        public HiwuImportFile? OriginalFile { get; set; }
    }
}
