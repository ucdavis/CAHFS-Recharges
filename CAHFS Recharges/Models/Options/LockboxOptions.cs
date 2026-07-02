using CAHFS_Recharges.Models;

namespace CAHFS_Recharges.Models.Options
{
    /// BofA Lockbox SFTP ingest and alert settings (bind from "Lockbox" configuration section).
    /// SFTP credentials from AWS SSM /{Environment}/Credentials/LockBox (JSON) or LockBoxUsername/LockBoxPassword (flat).
    /// Other settings bind from the Lockbox section in appsettings.
    public class LockboxOptions
    {
        public const string SectionName = "Lockbox";

        public bool Enabled { get; set; } = true;

        ///Hangfire cron for nightly ingest (default 3:00 AM).
        public string CronDaily { get; set; } = "0 3 * * *";

        /// Hangfire cron for parse/staging (default 3:15 AM; also runs after ingest).
        public string CronProcess { get; set; } = "15 3 * * *";

        public string TimeZone { get; set; } = "America/Los_Angeles";

        public string RemotePath { get; set; } = "/outgoing";

        public int ConnectTimeoutSeconds { get; set; } = 30;

        public int MaxFilesPerRun { get; set; } = 20;

        ///Alert when consecutive missing business days exceeds this value (e.g. 3 = alert on 4th day).
        public int MissingFileAlertAfterBusinessDays { get; set; } = 3;

        public LockboxLabOptions Cahfs { get; set; } = new() { LockboxId = "744833", FileNamePrefix = "DATA_744833" };

        public LockboxLabOptions Equine { get; set; } = new() { LockboxId = "744835", FileNamePrefix = "DATA_744835" };

        public LockboxSftpOptions Sftp { get; set; } = new();

        public LockboxAlertOptions Alert { get; set; } = new();

        public LockboxLabOptions GetLab(IntegrationType integration) =>
            integration == IntegrationType.EQUINE ? Equine : Cahfs;
    }

    public class LockboxLabOptions
    {
        public string LockboxId { get; set; } = "";
        public string FileNamePrefix { get; set; } = "";
    }

    public class LockboxSftpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 22;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class LockboxAlertOptions
    {
        public bool Enabled { get; set; } = true;
        public string EmailTo { get; set; } = "";
        public int MinHoursBetweenAlerts { get; set; } = 24;
    }
}
