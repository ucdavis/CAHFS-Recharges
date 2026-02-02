namespace CAHFS_Recharges.Services
{
    public sealed class AeHttpTrace
    {
        public DateTime WhenUtc { get; set; }
        public string Method { get; set; } = "";
        public string Url { get; set; } = "";
        public string RequestHeaders { get; set; } = "";
        public string? RequestBody { get; set; }
        public int? StatusCode { get; set; }
        public string? ResponseHeaders { get; set; }
        public string? ResponseBody { get; set; }
        public string? Exception { get; set; }

        public string ToMultilineText()
        {
            return
                $"AE HTTP ERROR ({WhenUtc:O})\n" +
                $"Method: {Method}\n" +
                $"URL: {Url}\n" +
                $"Status: {(StatusCode.HasValue ? StatusCode.Value.ToString() : "(no response)")}\n" +
                $"RequestHeaders:\n{RequestHeaders}\n" +
                $"RequestBody:\n{RequestBody}\n" +
                $"ResponseHeaders:\n{ResponseHeaders}\n" +
                $"ResponseBody:\n{ResponseBody}\n" +
                $"Exception:\n{Exception}";
        }
    }
}
