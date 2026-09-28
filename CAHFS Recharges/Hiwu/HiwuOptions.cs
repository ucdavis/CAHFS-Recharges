namespace CAHFS_Recharges.Hiwu
{
    /// HIWU SFTP job settings. Host, port, username, and password or key come from
    /// Parameter Store (Credentials:HIWU_SFTP).
    public class HiwuOptions
    {
        public const string SectionName = "Hiwu";

        public bool Enabled { get; set; }

        /// Hangfire cron for the daily download. The job is registered only when Enabled is true.
        public string CronDaily { get; set; } = "0 3 * * *";

        public string TimeZone { get; set; } = "America/Los_Angeles";

        /// Remote folders HIWU uses for manifests. A file may be in either folder.
        public string[] RemotePaths { get; set; } = ["/Outgoing", "/Outgoing/processed"];

        /// Used only when no file date is stored yet. 0 downloads every file on that first run.
        public int MaxFileAgeDays { get; set; }

        public int ConnectTimeoutSeconds { get; set; } = 30;

        public HiwuSftpOptions Sftp { get; set; } = new();

        /// TraceFirst website, username, and password. Parameter Store only.
        public HiwuCoflOptions Cofl { get; set; } = new();
    }

    public class HiwuSftpOptions
    {
        public string Host { get; set; } = "";

        public int Port { get; set; }

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";

        public string Key { get; set; } = "";
    }

    public class HiwuCoflOptions
    {
        public string Website { get; set; } = "";

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";
    }
}
