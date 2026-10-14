using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CAHFS_Recharges.Authorization
{
    /// Authorization handler that checks username against config allowlists.
    /// Role hierarchy: Admin > Operator > Viewer.
    public class ConfigBasedRoleHandler : AuthorizationHandler<RoleRequirement>
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public ConfigBasedRoleHandler(IConfiguration configuration, IHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleRequirement requirement)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
                return Task.CompletedTask;

            var username = context.User.Identity.Name;
            if (string.IsNullOrWhiteSpace(username))
                return Task.CompletedTask;

            var userRole = GetUserRole(username, Load("Admins"), Load("Operators"));
            if (RoleSatisfiesRequirement(userRole, requirement.RequiredRole))
                context.Succeed(requirement);

            return Task.CompletedTask;
        }

        /// Reads the merged configuration and the appsettings files.
        /// A name written in appsettings.{Environment}.json stays allowed when a later source replaces the merged list.
        private HashSet<string> Load(string listName)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Add(names, _configuration.GetSection("Authorization:" + listName).Get<string[]>());
            AddFile(names, Path.Combine(_environment.ContentRootPath, "appsettings.json"), listName);
            AddFile(names, Path.Combine(_environment.ContentRootPath, "appsettings." + _environment.EnvironmentName + ".json"), listName);
            return names;
        }

        private static void AddFile(HashSet<string> names, string path, string listName)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!document.RootElement.TryGetProperty("Authorization", out var authorization)
                    || authorization.ValueKind != JsonValueKind.Object
                    || !authorization.TryGetProperty(listName, out var list)
                    || list.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        Add(names, item.GetString());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        private static void Add(HashSet<string> names, IEnumerable<string?>? values)
        {
            if (values == null)
                return;

            foreach (var value in values)
                Add(names, value);
        }

        private static void Add(HashSet<string> names, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var trimmed = value.Trim();
            names.Add(trimmed);
            names.Add(Normalize(trimmed));
        }

        private static string GetUserRole(string username, HashSet<string> admins, HashSet<string> operators)
        {
            if (Contains(admins, username))
                return CaeiRoles.Admin;

            if (Contains(operators, username))
                return CaeiRoles.Operator;

            return CaeiRoles.Viewer;
        }

        private static bool Contains(HashSet<string> names, string username)
        {
            var trimmed = username.Trim();
            return names.Contains(trimmed) || names.Contains(Normalize(trimmed));
        }

        /// CAS may return the Kerberos id or the same id with a domain.
        private static string Normalize(string value)
        {
            var slash = value.LastIndexOf('\\');
            if (slash >= 0 && slash < value.Length - 1)
                value = value[(slash + 1)..];

            var at = value.IndexOf('@');
            if (at > 0)
                value = value[..at];

            return value;
        }

        /// Checks if user's role meets or exceeds the required role.
        private static bool RoleSatisfiesRequirement(string userRole, string requiredRole)
        {
            return requiredRole switch
            {
                CaeiRoles.Viewer => true,
                CaeiRoles.Operator => userRole is CaeiRoles.Operator or CaeiRoles.Admin,
                CaeiRoles.Admin => userRole == CaeiRoles.Admin,
                _ => false
            };
        }
    }
}
