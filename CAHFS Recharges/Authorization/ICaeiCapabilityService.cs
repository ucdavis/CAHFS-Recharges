namespace CAHFS_Recharges.Authorization
{
    public interface ICaeiCapabilityService
    {
        CaeiRole? GetUserRole(string? username);

        bool IsInProductFamily(string? username, string family);

        bool IsInLab(string? username, string lab);

        bool HasCapability(string? username, string family, string capability);

        IReadOnlyList<string> GetCapabilities(string? username, string family);

        bool CanOpenWorkspace(string? username, string lab, string family);
    }
}
