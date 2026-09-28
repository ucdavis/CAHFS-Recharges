using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Hiwu
{
    /// Loads the HIWU SFTP connection from Parameter Store into HiwuOptions.Sftp.
    public sealed class HiwuOptionsConfiguration : IPostConfigureOptions<HiwuOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<HiwuOptionsConfiguration> _logger;

        public HiwuOptionsConfiguration(IConfiguration configuration, ILogger<HiwuOptionsConfiguration> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public void PostConfigure(string? name, HiwuOptions options)
        {
            HiwuSftpSettingsReader.Apply(_configuration, options.Sftp);
            ApplyCofl(options);

            if (!HiwuSftpSettingsReader.IsParameterPresent(_configuration))
            {
                _logger.LogWarning(
                    "HIWU SFTP parameter {Parameter} is missing. Host, port, username, and password or key are not read from appsettings.",
                    HiwuCredentialPaths.ConfigurationPath);
                return;
            }

            var missing = HiwuSftpSettingsReader.GetMissingFields(options.Sftp);
            if (missing.Count > 0)
            {
                _logger.LogWarning(
                    "HIWU SFTP parameter {Parameter} is missing {Fields}.",
                    HiwuCredentialPaths.ConfigurationPath,
                    string.Join(", ", missing));
                return;
            }

            _logger.LogInformation(
                "HIWU SFTP settings loaded from {Parameter}.",
                HiwuCredentialPaths.ConfigurationPath);
        }

        private void ApplyCofl(HiwuOptions options)
        {
            HiwuCoflSettingsReader.Apply(_configuration, options.Cofl);

            if (!HiwuCoflSettingsReader.IsParameterPresent(_configuration))
            {
                _logger.LogWarning(
                    "HIWU COFL parameter {Parameter} is missing. Website, username, and password are not read from appsettings.",
                    HiwuCoflCredentialPaths.ConfigurationPath);
                return;
            }

            var missing = HiwuCoflSettingsReader.GetMissingFields(options.Cofl);
            if (missing.Count > 0)
            {
                _logger.LogWarning(
                    "HIWU COFL parameter {Parameter} is missing {Fields}.",
                    HiwuCoflCredentialPaths.ConfigurationPath,
                    string.Join(", ", missing));
                return;
            }

            _logger.LogInformation(
                "HIWU COFL settings loaded from {Parameter}.",
                HiwuCoflCredentialPaths.ConfigurationPath);
        }
    }
}
