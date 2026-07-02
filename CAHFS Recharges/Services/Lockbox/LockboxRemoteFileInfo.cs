namespace CAHFS_Recharges.Services.Lockbox
{
    public sealed class LockboxRemoteFileInfo
    {
        public string Name { get; init; } = "";
        public long Size { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }
        public bool IsDirectory { get; init; }
    }
}
