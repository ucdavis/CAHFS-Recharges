using Microsoft.Extensions.Configuration;
using System;
using System.Data.SqlClient;

namespace LockboxCashReceiptPoster
{

    /// Resolves connection strings using the same keys CAEI uses.
    /// Accepts both FinancialDb / FinancialDB spellings used across env files.
    internal static class ConnectionStringResolver
    {
        public static string? Resolve(IConfiguration configuration, string preferredName)
        {
            if (string.IsNullOrWhiteSpace(preferredName))
                return null;

            var value = configuration.GetConnectionString(preferredName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            // CAEI env files often use FinancialDB / EquineFinancialDB (capital DB).
            foreach (var alt in AlternateNames(preferredName))
            {
                value = configuration.GetConnectionString(alt);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            // Also allow env vars: ConnectionStrings__FinancialDb
            value = configuration[$"ConnectionStrings:{preferredName}"];
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return null;
        }

        /// <summary>
        /// eConnect CreateTransactionEntity requires Integrated Security (Windows auth).
        /// Staging reads still use the original SSM SQL-auth string; only the eConnect
        /// call uses this rewritten string (same Data Source / Initial Catalog).
        /// </summary>
        public static string ToEConnectIntegratedSecurity(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                IntegratedSecurity = true,
                PersistSecurityInfo = false,
                UserID = "",
                Password = ""
            };

            // Drop leftover SQL-auth tokens some builders keep when UserID is cleared.
            builder.Remove("User ID");
            builder.Remove("UID");
            builder.Remove("Password");
            builder.Remove("PWD");

            return builder.ConnectionString;
        }

        private static string[] AlternateNames(string preferredName)
        {
            if (preferredName.Equals("FinancialDb", StringComparison.OrdinalIgnoreCase))
                return new[] { "FinancialDB", "FinancialDb" };
            if (preferredName.Equals("EquineFinancialDb", StringComparison.OrdinalIgnoreCase))
                return new[] { "EquineFinancialDB", "EquineFinancialDb" };
            return Array.Empty<string>();
        }
    }
}
