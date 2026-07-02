using System.Text.Json;
using CAHFS_Recharges.Models.Options;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Services.Lockbox
{
    /// Reads Lockbox SFTP credentials from SSM (flat Credentials keys or JSON SecureString at Credentials:LockBox).
    internal static class LockboxSftpSettingsReader
    {
        private static readonly string[] UsernameKeys =
            ["username", "Username", "UserName", "User"];

        private static readonly string[] PasswordKeys =
            ["password", "Password", "Pwd"];

        public static void Apply(IConfiguration configuration, LockboxSftpOptions target)
        {
            var username = GetCredential(configuration, LockboxCredentialPaths.UsernameKey)
                ?? GetNestedCredential(configuration, LockboxCredentialPaths.NestedUsernameKey);

            var password = GetCredential(configuration, LockboxCredentialPaths.PasswordKey)
                ?? GetNestedCredential(configuration, LockboxCredentialPaths.NestedPasswordKey);

            if (!string.IsNullOrWhiteSpace(username))
                target.Username = username;
            if (!string.IsNullOrWhiteSpace(password))
                target.Password = password;

            if (!HasRequiredCredentials(target))
                TryApplyLockBoxJsonBlob(configuration, target);
        }

        public static bool HasRequiredCredentials(LockboxSftpOptions target) =>
            !string.IsNullOrWhiteSpace(target.Username) && !string.IsNullOrWhiteSpace(target.Password);

        /// Credentials section + setting name (same keys as AE; uses injected IConfiguration).
        public static string? GetCredential(IConfiguration configuration, string settingName) =>
            configuration
                .GetSection(LockboxCredentialPaths.CredentialsSection)
                .GetValue<string>(settingName);

        private static string? GetNestedCredential(IConfiguration configuration, string settingName) =>
            configuration.GetSection(LockboxCredentialPaths.NestedSection)[settingName];

        /// Parse /{Environment}/Credentials/LockBox SecureString JSON.
        private static void TryApplyLockBoxJsonBlob(IConfiguration configuration, LockboxSftpOptions target)
        {
            var json = configuration[LockboxCredentialPaths.NestedSection]
                ?? configuration.GetSection(LockboxCredentialPaths.NestedSection).Value;

            if (string.IsNullOrWhiteSpace(json))
                return;

            TryParseJsonCredentials(json, target);
        }

        private static void TryParseJsonCredentials(string json, LockboxSftpOptions target)
        {
            json = json.Trim();
            if (!json.StartsWith('{'))
                return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (UsernameKeys.Any(k => prop.Name.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        var value = ReadStringValue(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Username = value;
                    }
                    else if (PasswordKeys.Any(k => prop.Name.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        var value = ReadStringValue(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Password = value;
                    }
                }
            }
            catch (JsonException)
            {
                // Invalid JSON in SSM — must be {"username":"...","password":"..."}
            }
        }

        private static string? ReadStringValue(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null
            };

        public static bool IsLockBoxJsonPresent(IConfiguration configuration)
        {
            var json = configuration[LockboxCredentialPaths.NestedSection]
                ?? configuration.GetSection(LockboxCredentialPaths.NestedSection).Value;
            return !string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith('{');
        }

        public static bool IsLockBoxJsonValid(IConfiguration configuration)
        {
            var json = configuration[LockboxCredentialPaths.NestedSection]
                ?? configuration.GetSection(LockboxCredentialPaths.NestedSection).Value;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// Credential key names under Credentials (for diagnostics; no values).
        public static IReadOnlyList<string> GetCredentialsKeyNames(IConfiguration configuration) =>
            configuration.GetSection(LockboxCredentialPaths.CredentialsSection)
                .GetChildren()
                .Select(c => c.Key)
                .Where(k => k.Contains("Lock", StringComparison.OrdinalIgnoreCase))
                .ToList();
    }
}
