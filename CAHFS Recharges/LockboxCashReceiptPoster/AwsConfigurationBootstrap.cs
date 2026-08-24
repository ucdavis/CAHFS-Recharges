using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace LockboxCashReceiptPoster
{
    /// Same credential path as CAEI web app: optional awscredentials.xml → SDK profile,
    /// then Systems Manager Parameter Store /{Environment} and /Shared.
    internal static class AwsConfigurationBootstrap
    {
        public static IConfigurationRoot Build(string environmentName, Action<string>? log = null)
        {
            void Write(string message) => log?.Invoke(message);

            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var builder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();

            // Load JSON first so AWS:Profile / ProfilesLocation are available.
            var preliminary = builder.Build();

            var profileName = preliminary["AWS:Profile"] ?? "cahfs";
            var profilesLocation = preliminary["AWS:ProfilesLocation"];
            var credentialsXmlPath = preliminary["AWS:CredentialsXmlPath"];
            if (string.IsNullOrWhiteSpace(credentialsXmlPath))
                credentialsXmlPath = Path.Combine(basePath, "awscredentials.xml");

            // Only point the SDK at ProfilesLocation when the shared-credentials file actually exists.
            // A missing path breaks profile lookup and hides a previously registered NetSDK (XML) profile.
            if (!string.IsNullOrWhiteSpace(profilesLocation) && File.Exists(profilesLocation))
            {
                AWSConfigs.AWSProfilesLocation = profilesLocation;
                Write($"AWS profiles location: {profilesLocation}");
            }
            else if (!string.IsNullOrWhiteSpace(profilesLocation))
            {
                Write($"AWS profiles file not found yet: {profilesLocation} (will use NetSDK / xml if present)");
            }

            if (File.Exists(credentialsXmlPath))
            {
                Write($"Registering AWS profile '{profileName}' from {credentialsXmlPath}");
                RegisterProfileFromXml(credentialsXmlPath!, profileName, profilesLocation, Write);
            }

            var credentials = ResolveCredentials(profileName, profilesLocation, Write);
            // Avoid confusing fall-through to EC2 IMDS on non-EC2 hosts (GP RDP).
            Environment.SetEnvironmentVariable("AWS_EC2_METADATA_DISABLED", "true");

            var awsOptions = new AWSOptions
            {
                Region = RegionEndpoint.USWest1,
                Profile = profileName,
                Credentials = credentials
            };

            try
            {
                builder
                    .AddSystemsManager("/" + environmentName, awsOptions)
                    .AddSystemsManager("/Shared", awsOptions);
                Write($"Loaded AWS Parameter Store paths: /{environmentName}, /Shared");
            }
            catch (Exception ex)
            {
                Write("Failed to load secrets from AWS Parameter Store: " + (ex.InnerException?.Message ?? ex.Message));
                throw;
            }

            return builder.Build();
        }

        private static AWSCredentials ResolveCredentials(string profileName, string? profilesLocation, Action<string> write)
        {
            // 1) Shared credentials file (Task Scheduler–friendly, same path as appsettings).
            if (!string.IsNullOrWhiteSpace(profilesLocation) && File.Exists(profilesLocation))
            {
                var shared = new SharedCredentialsFile(profilesLocation);
                if (TryCredentialsFromProfileSource(shared, profileName, out var fromShared))
                {
                    write($"Using AWS profile '{profileName}' from {profilesLocation}.");
                    return fromShared!;
                }
            }

            // 2) NetSDK store (where CAEI / awscredentials.xml RegisterProfile writes).
            var netSdk = new NetSDKCredentialsFile();
            if (TryCredentialsFromProfileSource(netSdk, profileName, out var fromNetSdk))
            {
                write($"Using AWS profile '{profileName}' from NetSDK credentials store.");
                return fromNetSdk!;
            }

            // 3) Default chain (e.g. %USERPROFILE%\.aws\credentials).
            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(profileName, out var fromChain) && fromChain != null)
            {
                write($"Using AWS profile '{profileName}' from default credential chain.");
                return fromChain;
            }

            throw new InvalidOperationException(
                "AWS profile '" + profileName + "' was not found. " +
                "Place awscredentials.xml next to the exe once (AccessKeyId / SecretAccessKey / RegionEndpoint), " +
                "or create a shared-credentials file at C:\\cahfs-caei\\awscredentials with a [" + profileName + "] section. " +
                "Do not rely on EC2 instance metadata on this machine.");
        }

        private static bool TryCredentialsFromProfileSource(
            ICredentialProfileSource source,
            string profileName,
            out AWSCredentials? credentials)
        {
            credentials = null;
            if (!source.TryGetProfile(profileName, out var profile) || profile == null)
                return false;
            if (!profile.CanCreateAWSCredentials)
                return false;

            credentials = profile.GetAWSCredentials(source);
            return credentials != null;
        }

        /// <summary>Mirrors CAEI Program.SetAwsCredentials (xml → NetSDKCredentialsFile profile).</summary>
        private static void RegisterProfileFromXml(
            string xmlPath,
            string profileName,
            string? profilesLocation,
            Action<string> write)
        {
            var xAwsCredentials = XElement.Load(xmlPath, LoadOptions.None);
            var accessKey = xAwsCredentials.Element("AccessKeyId")?.Value?.Trim();
            var secretKey = xAwsCredentials.Element("SecretAccessKey")?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                throw new FormatException(
                    $"Could not parse AWS Credentials File: \"{xmlPath}\". AccessKeyId and/or SecretAccessKey are blank.");
            }

            var options = new CredentialProfileOptions
            {
                AccessKey = accessKey,
                SecretKey = secretKey
            };

            var profile = new CredentialProfile(profileName, options);
            var regionName = xAwsCredentials.Element("RegionEndpoint")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(regionName))
            {
                var field = typeof(RegionEndpoint).GetField(regionName, BindingFlags.Public | BindingFlags.Static);
                profile.Region = field?.GetValue(null) as RegionEndpoint ?? RegionEndpoint.USWest1;
            }
            else
            {
                profile.Region = RegionEndpoint.USWest1;
            }

            var netSdkFile = new NetSDKCredentialsFile();
            netSdkFile.RegisterProfile(profile);

            // Also write C:\cahfs-caei\awscredentials so later runs (and x86) do not depend only on NetSDK.
            if (!string.IsNullOrWhiteSpace(profilesLocation))
            {
                try
                {
                    var dir = Path.GetDirectoryName(profilesLocation);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    var shared = new SharedCredentialsFile(profilesLocation);
                    shared.RegisterProfile(profile);
                    write($"Also wrote shared credentials file: {profilesLocation}");
                    AWSConfigs.AWSProfilesLocation = profilesLocation;
                }
                catch (Exception ex)
                {
                    write($"Could not write shared credentials file '{profilesLocation}': {ex.Message}");
                }
            }

            try
            {
                File.Delete(xmlPath);
            }
            catch
            {
                write($"COULD NOT DELETE THE AWS CREDENTIALS XML FILE (\"{xmlPath}\"). Delete it manually.");
            }
        }
    }
}
