namespace CAHFS_Recharges.Hiwu
{
    /// AWS Parameter Store path for the TraceFirst COFL API login.
    /// SSM: /{Environment}/Credentials/HIWU-COFL-API → Credentials:HIWU-COFL-API
    public static class HiwuCoflCredentialPaths
    {
        public const string ParameterKey = "HIWU-COFL-API";

        public const string ConfigurationPath = "Credentials:HIWU-COFL-API";
    }
}
