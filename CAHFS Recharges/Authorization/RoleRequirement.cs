using Microsoft.AspNetCore.Authorization;

namespace CAHFS_Recharges.Authorization
{
    /// Represents a role-based authorization requirement.
    public class RoleRequirement : IAuthorizationRequirement
    {
        public string RequiredRole { get; }

        public RoleRequirement(string requiredRole)
        {
            RequiredRole = requiredRole;
        }
    }

    public static class CaeiRoles
    {
        public const string Viewer = "Viewer";
        public const string Operator = "Operator";
        public const string Admin = "Admin";
    }

    public static class CaeiPolicies
    {
        /// Layer 0: authenticated CAS user on Admins, Operators, or Viewers.
        public const string AppUserPolicy = "AppUserPolicy";

        public const string ViewerPolicy = "ViewerPolicy";
        public const string OperatorPolicy = "OperatorPolicy";
        public const string AdminPolicy = "AdminPolicy";
    }
}

