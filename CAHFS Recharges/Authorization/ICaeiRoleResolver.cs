namespace CAHFS_Recharges.Authorization
{
    public interface ICaeiRoleResolver
    {
        /// Returns the user's role, or null when the Kerberos ID is not on Admins, Operators, or Viewers.
        CaeiRole? TryResolveRole(string? username);
    }
}
