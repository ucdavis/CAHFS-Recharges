namespace CAHFS_Recharges.Hiwu
{
    public static class HiwuParseStatus
    {
        public const string Stored = "Stored";
        public const string Received = "Received";
        public const string ParseError = "ParseError";
        public const string Amended = "Amended";

        public static string Label(string? status) => status switch
        {
            Received => "Received",
            ParseError => "Parse error",
            Amended => "Amended",
            Stored => "Stored",
            _ => status ?? ""
        };
    }

    public static class HiwuIngestRunStatus
    {
        public const string Completed = "Completed";
        public const string NoFiles = "NoFiles";
        public const string Failed = "Failed";
    }
}
