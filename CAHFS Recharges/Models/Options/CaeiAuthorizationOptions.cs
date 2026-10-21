namespace CAHFS_Recharges.Models.Options
{
    /// AWS Parameter Store contract for CAEI authorization (SSM /{Env}/Authorization).
    /// Put each Kerberos ID in exactly one of Admins, Operators, or Viewers (Admin wins if duplicated).
    /// Actions follow role hierarchy in code; workspace access uses ProductFamilies + Labs.
    public class CaeiAuthorizationOptions
    {
        public const string SectionName = "Authorization";

        /// Full app access + all workspaces. Do not also list these IDs under Operators/Viewers.
        public List<string> Admins { get; set; } = new();

        /// Mutate/send within granted ProductFamilies + Labs. Do not also list under Admins/Viewers.
        public List<string> Operators { get; set; } = new();

        /// Read-only within granted ProductFamilies + Labs. Do not also list under Admins/Operators.
        public List<string> Viewers { get; set; } = new();

        /// Keys: AE, Lockbox, Hiwu
        public Dictionary<string, List<string>> ProductFamilies { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// Keys: CAHFS, EQUINE
        public Dictionary<string, List<string>> Labs { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
