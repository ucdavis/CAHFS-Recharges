using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Hiwu
{
    /// Reads the HIWU SFTP connection from Credentials:HIWU_SFTP only.
    internal static class HiwuSftpSettingsReader
    {
        public static void Apply(IConfiguration configuration, HiwuSftpOptions target)
        {
            var json = configuration[HiwuCredentialPaths.ConfigurationPath]
                ?? configuration.GetSection(HiwuCredentialPaths.ConfigurationPath).Value;

            if (string.IsNullOrWhiteSpace(json))
                return;

            TryParse(json, target);
        }

        public static IReadOnlyList<string> GetMissingFields(HiwuSftpOptions target)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(target.Host))
                missing.Add("host");
            if (target.Port <= 0)
                missing.Add("port");
            if (string.IsNullOrWhiteSpace(target.Username))
                missing.Add("username");
            if (string.IsNullOrWhiteSpace(target.Password) && string.IsNullOrWhiteSpace(target.Key))
                missing.Add("password or key");
            return missing;
        }

        public static bool IsParameterPresent(IConfiguration configuration)
        {
            var json = configuration[HiwuCredentialPaths.ConfigurationPath]
                ?? configuration.GetSection(HiwuCredentialPaths.ConfigurationPath).Value;
            return !string.IsNullOrWhiteSpace(json);
        }

        private static void TryParse(string json, HiwuSftpOptions target)
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
                    if (prop.Name.Equals("host", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ReadString(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Host = value;
                    }
                    else if (prop.Name.Equals("port", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadPort(prop.Value, out var port))
                            target.Port = port;
                    }
                    else if (prop.Name.Equals("username", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ReadString(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Username = value;
                    }
                    else if (prop.Name.Equals("password", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ReadString(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Password = value;
                    }
                    else if (prop.Name.Equals("key", StringComparison.OrdinalIgnoreCase)
                        || prop.Name.Equals("privateKey", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ReadString(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Key = value;
                    }
                }
            }
            catch (JsonException)
            {
                // Parameter must be JSON: host, port, username, and password or key.
            }
        }

        private static bool TryReadPort(JsonElement element, out int port)
        {
            port = 0;
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out port))
                return port > 0;

            var text = ReadString(element);
            return int.TryParse(text, out port) && port > 0;
        }

        private static string? ReadString(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null
            };
    }
}
