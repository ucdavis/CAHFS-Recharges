using CAHFS_Recharges.Models.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace CAHFS_Recharges.Services.Lockbox
{
    public sealed class LockboxSftpClient
    {
        private readonly LockboxOptions _options;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LockboxSftpClient> _logger;

        public LockboxSftpClient(
            IOptions<LockboxOptions> options,
            IConfiguration configuration,
            ILogger<LockboxSftpClient> logger)
        {
            _options = options.Value;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<LockboxRemoteFileInfo>> ListFilesAsync(
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

        private List<LockboxRemoteFileInfo> ListFilesCore(SftpClient client, string remotePath)
        {
            var path = NormalizeRemotePath(remotePath);
            var entries = client.ListDirectory(path);
            var results = new List<LockboxRemoteFileInfo>();

            foreach (var entry in entries)
            {
                if (entry.Name is "." or "..")
                    continue;

                results.Add(new LockboxRemoteFileInfo
                {
                    Name = entry.Name,
                    Size = entry.Attributes.Size,
                    LastWriteTimeUtc = entry.LastWriteTimeUtc,
                    IsDirectory = entry.IsDirectory
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
            var sftp = ResolveSftpSettings();
            var host = NormalizeHost(sftp.Host);
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("Lockbox SFTP Host is not configured (Lockbox:Sftp:Host).");
            if (string.IsNullOrWhiteSpace(sftp.Username))
            {
                LogCredentialDiagnostics();
                throw new InvalidOperationException(
                    "Lockbox SFTP Username is not configured. Expected SSM /{Environment}/Credentials/LockBox JSON " +
                    "({\"username\":\"...\",\"password\":\"...\"}) or flat Credentials/LockBoxUsername.");
            }
            if (string.IsNullOrWhiteSpace(sftp.Password))
            {
                LogCredentialDiagnostics();
                throw new InvalidOperationException(
                    "Lockbox SFTP Password is not configured. Expected SSM /{Environment}/Credentials/LockBox JSON or LockBoxPassword.");
            }

            var port = sftp.Port > 0 ? sftp.Port : 22;
            var timeout = TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds > 0 ? _options.ConnectTimeoutSeconds : 30);

            var client = new SftpClient(host, port, sftp.Username, sftp.Password)
            {
                OperationTimeout = timeout,
                ConnectionInfo = { Timeout = timeout }
            };

            _logger.LogInformation("Lockbox SFTP connecting to {Host}:{Port}", host, port);
            client.Connect();
            return client;
        }

        private static string NormalizeHost(string host)
        {
            host = host.Trim();
            if (host.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase))
                host = host[7..];
            return host.TrimEnd('/');
        }

        private LockboxSftpOptions ResolveSftpSettings()
        {
            var sftp = new LockboxSftpOptions
            {
                Host = _options.Sftp.Host,
                Port = _options.Sftp.Port,
                Username = _options.Sftp.Username,
                Password = _options.Sftp.Password
            };
            LockboxSftpSettingsReader.Apply(_configuration, sftp);
            return sftp;
        }

        private void LogCredentialDiagnostics()
        {
            var credKeys = string.Join(", ", LockboxSftpSettingsReader.GetCredentialsKeyNames(_configuration));
            var hasJson = LockboxSftpSettingsReader.IsLockBoxJsonPresent(_configuration);
            var jsonValid = LockboxSftpSettingsReader.IsLockBoxJsonValid(_configuration);

            _logger.LogWarning(
                "Lockbox SFTP credentials missing. SSM /{{Environment}}/Credentials/LockBox: jsonPresent={HasJson}, jsonValid={JsonValid}. " +
                "Value must be valid JSON: {{\"username\":\"...\",\"password\":\"...\"}} (both keys need colons). " +
                "Flat keys [{UsernameKey}/{PasswordKey}] also supported. Lock-related keys: [{CredKeys}].",
                hasJson,
                jsonValid,
                LockboxCredentialPaths.UsernameKey,
                LockboxCredentialPaths.PasswordKey,
                string.IsNullOrEmpty(credKeys) ? "(none)" : credKeys);
        }

        private static string NormalizeRemotePath(string path)
        {
            path = path.Trim();
            if (string.IsNullOrEmpty(path))
                return "/outgoing";
            return path.StartsWith('/') ? path : "/" + path;
        }
    }
}
