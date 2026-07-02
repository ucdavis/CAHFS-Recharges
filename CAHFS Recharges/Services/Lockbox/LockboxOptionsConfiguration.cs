using CAHFS_Recharges.Models.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Services.Lockbox
{
    /// Merges SFTP credentials from AWS Parameter Store (Credentials:LockBoxUsername / LockBoxPassword) into LockboxOptions.Sftp.
    public sealed class LockboxOptionsConfiguration : IPostConfigureOptions<LockboxOptions>
    {
        private readonly IConfiguration _configuration;

        public LockboxOptionsConfiguration(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void PostConfigure(string? name, LockboxOptions options) =>
            LockboxSftpSettingsReader.Apply(_configuration, options.Sftp);
    }
}
