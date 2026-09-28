using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuRemoteFile
    {
        public string Name { get; set; } = "";
        public bool IsDirectory { get; set; }
        public DateTime? LastWriteTimeUtc { get; set; }
    }

    /// HIWU SFTP only. 
    public sealed class HiwuSftpClient
    {
        private readonly HiwuOptions _options;
        private readonly ILogger<HiwuSftpClient> _logger;

        public HiwuSftpClient(IOptions<HiwuOptions> options, ILogger<HiwuSftpClient> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<HiwuRemoteFile>> ListFilesAsync(
            string remotePath,
            CancellationToken cancellationToken = default)
        {
            using var client = CreateConnectedClient();
            return await Task.Run(() => ListFilesCore(client, remotePath), cancellationToken);
        }

        public async Task<byte[]> DownloadFileAsync(
            string remotePath,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            using var client = CreateConnectedClient();
            return await Task.Run(() => DownloadFileCore(client, remotePath, fileName), cancellationToken);
        }

        private List<HiwuRemoteFile> ListFilesCore(SftpClient client, string remotePath)
        {
            var entries = client.ListDirectory(NormalizeRemotePath(remotePath));
            var results = new List<HiwuRemoteFile>();
            foreach (var entry in entries)
            {
                if (entry.Name is "." or "..")
                    continue;

                results.Add(new HiwuRemoteFile
                {
                    Name = entry.Name,
                    IsDirectory = entry.IsDirectory,
                    LastWriteTimeUtc = entry.LastWriteTimeUtc
                });
            }

            return results;
        }

        private static byte[] DownloadFileCore(SftpClient client, string remotePath, string fileName)
        {
            var fullPath = $"{NormalizeRemotePath(remotePath)}/{fileName}";
            using var stream = new MemoryStream();
            client.DownloadFile(fullPath, stream);
            return stream.ToArray();
        }

        private SftpClient CreateConnectedClient()
        {
            var sftp = _options.Sftp;
            var missing = HiwuSftpSettingsReader.GetMissingFields(sftp);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "HIWU SFTP settings are incomplete (" + string.Join(", ", missing) +
                    "). Expected Parameter Store " + HiwuCredentialPaths.ConfigurationPath + ".");
            }

            var host = NormalizeHost(sftp.Host);
            var port = sftp.Port;
            var timeout = TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds > 0 ? _options.ConnectTimeoutSeconds : 30);
            var connection = CreateConnection(host, port, sftp);
            connection.Timeout = timeout;

            var client = new SftpClient(connection)
            {
                OperationTimeout = timeout
            };

            _logger.LogInformation("HIWU SFTP connecting to {Host}:{Port}", host, port);
            client.Connect();
            return client;
        }

        private static Renci.SshNet.ConnectionInfo CreateConnection(string host, int port, HiwuSftpOptions sftp)
        {
            if (!string.IsNullOrWhiteSpace(sftp.Key))
            {
                using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(sftp.Key));
                var keyFile = new PrivateKeyFile(keyStream);
                return new Renci.SshNet.ConnectionInfo(host, port, sftp.Username, new PrivateKeyAuthenticationMethod(sftp.Username, keyFile));
            }

            return new Renci.SshNet.ConnectionInfo(
                host,
                port,
                sftp.Username,
                new PasswordAuthenticationMethod(sftp.Username, sftp.Password));
        }

        private static string NormalizeHost(string host)
        {
            host = host.Trim();
            if (host.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
                host = host[7..];
            return host.TrimEnd('/');
        }

        private static string NormalizeRemotePath(string path)
        {
            path = path.Trim();
            if (string.IsNullOrEmpty(path))
                return "/Outgoing";
            return path.StartsWith('/') ? path : "/" + path;
        }
    }
}
