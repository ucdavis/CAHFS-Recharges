using CAHFS_Recharges.Models.Options;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Authorization
{
    /// Workspace membership from ProductFamilies + Labs (same IConfiguration/SSM chain as roles).
    /// Actions from fixed role defaults: Viewer read-only; Operator mutate/send; Admin all.
    public sealed class CaeiCapabilityService : ICaeiCapabilityService
    {
        private static readonly string[] ViewerCaps =
        {
            CaeiCapabilities.Read,
            CaeiCapabilities.Filter,
            CaeiCapabilities.Export
        };

        private static readonly string[] OperatorCaps =
        {
            CaeiCapabilities.Read,
            CaeiCapabilities.Filter,
            CaeiCapabilities.Export,
            CaeiCapabilities.Validate,
            CaeiCapabilities.PostBatch,
            CaeiCapabilities.Mutate
        };

        private static readonly string[] AdminCaps =
        {
            CaeiCapabilities.Read,
            CaeiCapabilities.Filter,
            CaeiCapabilities.Export,
            CaeiCapabilities.Validate,
            CaeiCapabilities.PostBatch,
            CaeiCapabilities.Mutate,
            CaeiCapabilities.AdminTools
        };

        private readonly ICaeiRoleResolver _roleResolver;
        private readonly IOptionsMonitor<CaeiAuthorizationOptions> _options;

        public CaeiCapabilityService(
            ICaeiRoleResolver roleResolver,
            IOptionsMonitor<CaeiAuthorizationOptions> options)
        {
            _roleResolver = roleResolver;
            _options = options;
        }

        public CaeiRole? GetUserRole(string? username) => _roleResolver.TryResolveRole(username);

        public bool IsInProductFamily(string? username, string family)
        {
            var role = GetUserRole(username);
            if (role == null)
                return false;

            if (role == CaeiRole.Admin)
                return true;

            return IsListed(_options.CurrentValue.ProductFamilies, family, username!);
        }

        public bool IsInLab(string? username, string lab)
        {
            var role = GetUserRole(username);
            if (role == null)
                return false;

            if (role == CaeiRole.Admin)
                return true;

            return IsListed(_options.CurrentValue.Labs, lab, username!);
        }

        public bool HasCapability(string? username, string family, string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
                return false;

            var role = GetUserRole(username);
            if (role == null)
                return false;

            if (role == CaeiRole.Admin)
                return true;

            if (!IsInProductFamily(username, family))
                return false;

            return CapsFor(role.Value).Any(c =>
                string.Equals(c, capability, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> GetCapabilities(string? username, string family)
        {
            var role = GetUserRole(username);
            if (role == null)
                return Array.Empty<string>();

            if (role == CaeiRole.Admin)
                return AdminCaps;

            if (!IsInProductFamily(username, family))
                return Array.Empty<string>();

            return CapsFor(role.Value);
        }

        public bool CanOpenWorkspace(string? username, string lab, string family)
        {
            return IsInProductFamily(username, family) && IsInLab(username, lab);
        }

        private static IReadOnlyList<string> CapsFor(CaeiRole role) => role switch
        {
            CaeiRole.Admin => AdminCaps,
            CaeiRole.Operator => OperatorCaps,
            _ => ViewerCaps
        };

        private static bool IsListed(Dictionary<string, List<string>>? map, string key, string username)
        {
            if (map == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (!TryGetIgnoreCase(map, key, out var names) || names == null)
                return false;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var trimmed = name.Trim();
                set.Add(trimmed);
                set.Add(CaeiRoleResolver.Normalize(trimmed));
            }

            return CaeiRoleResolver.Contains(set, username);
        }

        private static bool TryGetIgnoreCase<T>(Dictionary<string, T> map, string key, out T? value)
        {
            if (map.TryGetValue(key, out value!))
                return true;

            foreach (var pair in map)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
