namespace CAHFS_Recharges.Models.Options
{
    /// AWS Parameter Store paths for BofA Lockbox SFTP credentials.
    public static class LockboxCredentialPaths
    {
        public const string CredentialsSection = "Credentials";

        /// SSM: /{Environment}/Credentials/LockBoxUsername → Credentials:LockBoxUsername
        public const string UsernameKey = "LockBoxUsername";

        /// SSM: /{Environment}/Credentials/LockBoxPassword → Credentials:LockBoxPassword
        public const string PasswordKey = "LockBoxPassword";

        /// Legacy nested path if parent LockBox parameter is not used.
        public const string NestedSection = "Credentials:LockBox";

        public const string NestedUsernameKey = "Username";
        public const string NestedPasswordKey = "Password";
    }
}
