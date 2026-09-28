using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Hiwu
{
    /// Reads the TraceFirst login from Credentials:HIWU-COFL-API only.
    /// JSON keys: website, username, password.
    internal static class HiwuCoflSettingsReader
    {
        public static void Apply(IConfiguration configuration, HiwuCoflOptions target)
        {
            var json = configuration[HiwuCoflCredentialPaths.ConfigurationPath]
                ?? configuration.GetSection(HiwuCoflCredentialPaths.ConfigurationPath).Value;

            if (string.IsNullOrWhiteSpace(json))
                return;

            TryParse(json, target);
        }

        public static IReadOnlyList<string> GetMissingFields(HiwuCoflOptions target)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(target.Website))
                missing.Add("website");
            if (string.IsNullOrWhiteSpace(target.Username))
                missing.Add("username");
            if (string.IsNullOrWhiteSpace(target.Password))
                missing.Add("password");
            return missing;
        }

        public static bool IsParameterPresent(IConfiguration configuration)
        {
            var json = configuration[HiwuCoflCredentialPaths.ConfigurationPath]
                ?? configuration.GetSection(HiwuCoflCredentialPaths.ConfigurationPath).Value;
            return !string.IsNullOrWhiteSpace(json);
        }

        private static void TryParse(string json, HiwuCoflOptions target)
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
                    if (prop.Name.Equals("website", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ReadString(prop.Value);
                        if (!string.IsNullOrWhiteSpace(value))
                            target.Website = value;
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
                }
            }
            catch (JsonException)
            {
                // Parameter must be JSON: website, username, and password.
            }
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
