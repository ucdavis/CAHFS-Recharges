namespace CAHFS_Recharges.Hiwu
{
    /// AWS Parameter Store path for the HIWU SFTP connection.
    /// SSM: /{Environment}/Credentials/HIWU_SFTP → Credentials:HIWU_SFTP
    public static class HiwuCredentialPaths
    {
        public const string CredentialsSection = "Credentials";

        public const string ParameterKey = "HIWU_SFTP";

        public const string ConfigurationPath = "Credentials:HIWU_SFTP";
    }
}
