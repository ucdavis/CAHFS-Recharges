using CAHFS_Recharges.Models.Options;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Authorization
{
    /// Resolves CAEI roles from IConfiguration Authorization lists only
    /// (appsettings → env appsettings → AWS SSM). One source chain; Admin wins over Operator over Viewer.
    public sealed class CaeiRoleResolver : ICaeiRoleResolver
    {
        private readonly IOptionsMonitor<CaeiAuthorizationOptions> _options;

        public CaeiRoleResolver(IOptionsMonitor<CaeiAuthorizationOptions> options)
        {
            _options = options;
        }

        public CaeiRole? TryResolveRole(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            var opts = _options.CurrentValue;

            if (IsInList(opts.Admins, username))
                return CaeiRole.Admin;

            if (IsInList(opts.Operators, username))
                return CaeiRole.Operator;

            if (IsInList(opts.Viewers, username))
                return CaeiRole.Viewer;

            return null;
        }

        private static bool IsInList(IEnumerable<string>? names, string username)
        {
            if (names == null)
                return false;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var trimmed = name.Trim();
                set.Add(trimmed);
                set.Add(Normalize(trimmed));
            }

            return Contains(set, username);
        }

        internal static bool Contains(HashSet<string> names, string username)
        {
            var trimmed = username.Trim();
            return names.Contains(trimmed) || names.Contains(Normalize(trimmed));
        }

        /// CAS may return the Kerberos id or the same id with a domain.
        internal static string Normalize(string value)
        {
            var slash = value.LastIndexOf('\\');
            if (slash >= 0 && slash < value.Length - 1)
                value = value[(slash + 1)..];

            var at = value.IndexOf('@');
            if (at > 0)
                value = value[..at];

            return value;
        }
    }
}
