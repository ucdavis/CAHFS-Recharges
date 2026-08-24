using System.Text;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Models.Options;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Services.Lockbox
{
    public sealed class LockboxReadService
    {
        private const int DefaultMaxRows = 1000;

        private readonly LockboxOptions _options;
        private readonly FinancialContext _cahfsContext;
        private readonly EquineFinancialContext _equineContext;

        public LockboxReadService(
            IOptions<LockboxOptions> options,
            FinancialContext cahfsContext,
            EquineFinancialContext equineContext)
        {
            _options = options.Value;
            _cahfsContext = cahfsContext;
            _equineContext = equineContext;
        }

        public async Task<LockboxStagingSummary> GetStagingSummaryAsync(
            IntegrationType integration,
            DateTime? fromDate,
            DateTime? toDate,
            string? validationStatus,
            string? fileName,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var (whereClause, parameters) = BuildWhereClause(integration, fromDate, toDate, validationStatus, fileName);

            var sql = $"""
                SELECT
                    COUNT(*) AS TotalCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'VALID' THEN 1 ELSE 0 END), 0) AS ValidCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'WARN' THEN 1 ELSE 0 END), 0) AS WarnCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'ERROR' THEN 1 ELSE 0 END), 0) AS ErrorCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'PENDING' THEN 1 ELSE 0 END), 0) AS PendingCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'INVALID_CUSTOMER' THEN 1 ELSE 0 END), 0) AS InvalidCustomerCount,
                    ISNULL(SUM(CASE WHEN IsPrimaryAddenda = 1 THEN CheckAmount ELSE 0 END), 0) AS TotalCheckAmount
                FROM dbo.C_LB_Payment_Staging
                {whereClause}
                """;

            var rows = await context.Database
                .SqlQueryRaw<LockboxStagingSummary>(sql, parameters)
                .ToListAsync(cancellationToken);

            return rows.FirstOrDefault() ?? new LockboxStagingSummary();
        }

        public async Task<IReadOnlyList<LockboxPaymentStagingRow>> GetStagingRowsAsync(
            IntegrationType integration,
            DateTime? fromDate,
            DateTime? toDate,
            string? validationStatus,
            string? fileName,
            int maxRows = DefaultMaxRows,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var (whereClause, parameters) = BuildWhereClause(integration, fromDate, toDate, validationStatus, fileName);

            var sql = $"""
                SELECT TOP (@maxRows)
                    CAST(TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) AS VARCHAR(20)) AS LockboxNumber,
                    DepositDate,
                    BatchNumber,
                    SeqNumber,
                    BankNumber,
                    AccountNumber,
                    CheckNumber,
                    RemitterName,
                    CheckAmount,
                    BillingId,
                    AccessionFull,
                    InvAmt,
                    ValidationStatus,
                    ValidationNotes
                FROM dbo.C_LB_Payment_Staging
                {whereClause}
                ORDER BY DepositDate DESC, BatchNumber, SeqNumber, AddendaSeq
                """;

            var allParameters = new List<object> { new SqlParameter("@maxRows", maxRows) };
            allParameters.AddRange(parameters);

            return await context.Database
                .SqlQueryRaw<LockboxPaymentStagingRow>(sql, allParameters.ToArray())
                .ToListAsync(cancellationToken);
        }

        public async Task<LockboxPostHistorySummary> GetPostHistorySummaryAsync(
            IntegrationType integration,
            DateTime? postedFrom,
            DateTime? postedTo,
            string? postStatus,
            string? billingId,
            string? documentNumber,
            string? fileName,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var (whereClause, parameters) = BuildPostHistoryWhereClause(
                integration, postedFrom, postedTo, postStatus, billingId, documentNumber, fileName);

            var sql = $"""
                SELECT
                    ISNULL(SUM(CASE WHEN PostStatus = 'Posted' THEN 1 ELSE 0 END), 0) AS PostedCount,
                    ISNULL(SUM(CASE WHEN PostStatus = 'Failed' THEN 1 ELSE 0 END), 0) AS FailedCount,
                    ISNULL(SUM(CASE WHEN PostStatus = 'Posted' AND IsPrimaryAddenda = 1 THEN CheckAmount ELSE 0 END), 0) AS PostedAmount
                FROM dbo.C_LB_Payment_Staging
                {whereClause}
                """;

            var rows = await context.Database
                .SqlQueryRaw<LockboxPostHistorySummary>(sql, parameters)
                .ToListAsync(cancellationToken);

            return rows.FirstOrDefault() ?? new LockboxPostHistorySummary();
        }

        public async Task<IReadOnlyList<LockboxPostHistoryRow>> GetPostHistoryRowsAsync(
            IntegrationType integration,
            DateTime? postedFrom,
            DateTime? postedTo,
            string? postStatus,
            string? billingId,
            string? documentNumber,
            string? fileName,
            int maxRows = DefaultMaxRows,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var (whereClause, parameters) = BuildPostHistoryWhereClause(
                integration, postedFrom, postedTo, postStatus, billingId, documentNumber, fileName);

            var sql = $"""
                SELECT TOP (@maxRows)
                    StagingId,
                    PostedAt,
                    PostStatus,
                    BillingId,
                    CheckNumber,
                    CheckAmount,
                    DepositDate,
                    BatchNumber,
                    FileName,
                    ExternalDocNumber,
                    PostError
                FROM dbo.C_LB_Payment_Staging
                {whereClause}
                ORDER BY PostedAt DESC, StagingId DESC
                """;

            var allParameters = new List<object> { new SqlParameter("@maxRows", maxRows) };
            allParameters.AddRange(parameters);

            return await context.Database
                .SqlQueryRaw<LockboxPostHistoryRow>(sql, allParameters.ToArray())
                .ToListAsync(cancellationToken);
        }

        public async Task<LockboxCheckValidationSummary> GetCheckValidationSummaryAsync(
            IntegrationType integration,
            DateTime? fromDate,
            DateTime? toDate,
            string? billingId,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var (whereClause, parameters) = BuildCheckValidationWhereClause(
                integration, fromDate, toDate, statusFilter: null, billingId, exceptionsOnly: true);

            var sql = $"""
                SELECT
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'INVALID_CUSTOMER' THEN 1 ELSE 0 END), 0) AS InvalidCustomerCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'WARN' THEN 1 ELSE 0 END), 0) AS WarnCount,
                    ISNULL(SUM(CASE WHEN ValidationStatus = 'ERROR' THEN 1 ELSE 0 END), 0) AS ErrorCount
                FROM dbo.C_LB_Payment_Staging
                {whereClause}
                """;

            var rows = await context.Database
                .SqlQueryRaw<LockboxCheckValidationSummary>(sql, parameters)
                .ToListAsync(cancellationToken);

            return rows.FirstOrDefault() ?? new LockboxCheckValidationSummary();
        }

        public async Task<IReadOnlyList<LockboxCheckValidationRow>> GetCheckValidationRowsAsync(
            IntegrationType integration,
            DateTime? fromDate,
            DateTime? toDate,
            string? statusFilter,
            string? billingId,
            int maxRows = DefaultMaxRows,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var exceptionsOnly = string.IsNullOrWhiteSpace(statusFilter);
            var (whereClause, parameters) = BuildCheckValidationWhereClause(
                integration, fromDate, toDate, statusFilter, billingId, exceptionsOnly);

            var sql = $"""
                SELECT TOP (@maxRows)
                    StagingId,
                    DepositDate,
                    CheckNumber,
                    CheckAmount,
                    BillingId,
                    ValidationStatus,
                    ValidationNotes,
                    FileName,
                    PostStatus,
                    BatchNumber,
                    RemitterName,
                    AccessionFull
                FROM dbo.C_LB_Payment_Staging
                {whereClause}
                ORDER BY
                    DepositDate DESC,
                    CASE ValidationStatus
                        WHEN 'INVALID_CUSTOMER' THEN 1
                        WHEN 'WARN' THEN 2
                        WHEN 'ERROR' THEN 3
                        ELSE 4
                    END,
                    StagingId DESC
                """;

            var allParameters = new List<object> { new SqlParameter("@maxRows", maxRows) };
            allParameters.AddRange(parameters);

            return await context.Database
                .SqlQueryRaw<LockboxCheckValidationRow>(sql, allParameters.ToArray())
                .ToListAsync(cancellationToken);
        }

        /// Operator correction: trim/pad BillingId, match RM00101, set VALID or INVALID_CUSTOMER.
        /// Does not modify Posted rows.
        public async Task<LockboxBillingIdUpdateResult> UpdateBillingIdAsync(
            IntegrationType integration,
            int stagingId,
            string? billingId,
            string userName,
            CancellationToken cancellationToken = default)
        {
            var context = GetContext(integration);
            var lab = _options.GetLab(integration);
            var trimmedUser = string.IsNullOrWhiteSpace(userName) ? "unknown" : userName.Trim();

            const string loadSql = """
                SELECT TOP (1)
                    StagingId,
                    DepositDate,
                    CheckNumber,
                    CheckAmount,
                    BillingId,
                    ValidationStatus,
                    ValidationNotes,
                    FileName,
                    PostStatus,
                    BatchNumber,
                    RemitterName
                FROM dbo.C_LB_Payment_Staging
                WHERE StagingId = @stagingId
                  AND TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) IS NOT NULL
                  AND CAST(TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) AS VARCHAR(20)) = @lockboxId
                """;

            var existing = (await context.Database
                .SqlQueryRaw<LockboxCheckValidationRow>(
                    loadSql,
                    new SqlParameter("@stagingId", stagingId),
                    new SqlParameter("@lockboxId", lab.LockboxId))
                .ToListAsync(cancellationToken))
                .FirstOrDefault();

            if (existing == null)
                return LockboxBillingIdUpdateResult.Fail("Staging row was not found for this lockbox.");

            if (string.Equals(existing.PostStatus, "Posted", StringComparison.OrdinalIgnoreCase))
                return LockboxBillingIdUpdateResult.Fail("This payment was already posted to Great Plains and cannot be edited.");

            var trimmed = (billingId ?? "").Trim();
            if (trimmed.Length == 0)
            {
                const string blankSql = """
                    UPDATE dbo.C_LB_Payment_Staging
                    SET BillingId = NULL,
                        ValidationStatus = 'INVALID_CUSTOMER',
                        ValidationNotes = @notes
                    WHERE StagingId = @stagingId
                      AND ISNULL(PostStatus, '') <> 'Posted'
                    """;
                var blankNotes =
                    $"Billing ID cleared by {trimmedUser} at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Correct Billing ID before GP posting.";
                await context.Database.ExecuteSqlRawAsync(
                    blankSql,
                    new object[]
                    {
                        new SqlParameter("@notes", TruncateNotes(blankNotes)),
                        new SqlParameter("@stagingId", stagingId)
                    },
                    cancellationToken);
                return LockboxBillingIdUpdateResult.Ok("Billing ID cleared. Status set to INVALID_CUSTOMER.", becameValid: false);
            }

            var candidates = new List<string> { trimmed };
            if (trimmed.Length is >= 1 and <= 7)
                candidates.Add(trimmed.PadLeft(8, '0'));

            string? resolvedCust = null;
            foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                const string lookupSql = """
                    SELECT TOP (1) RTRIM(CUSTNMBR) AS Value
                    FROM dbo.RM00101
                    WHERE LEN(LTRIM(RTRIM(CUSTNMBR))) > 0
                      AND RTRIM(CUSTNMBR) = @cust
                    """;
                var match = await context.Database
                    .SqlQueryRaw<StringScalar>(lookupSql, new SqlParameter("@cust", candidate))
                    .ToListAsync(cancellationToken);
                if (match.Count > 0 && !string.IsNullOrWhiteSpace(match[0].Value))
                {
                    resolvedCust = match[0].Value.Trim();
                    break;
                }
            }

            if (resolvedCust != null)
            {
                var notes =
                    $"Billing ID corrected by {trimmedUser} at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Matched GP customer {resolvedCust}.";
                const string validSql = """
                    UPDATE dbo.C_LB_Payment_Staging
                    SET BillingId = @billingId,
                        ValidationStatus = 'VALID',
                        ValidationNotes = @notes,
                        PostStatus = CASE WHEN ISNULL(PostStatus, '') = 'Failed' THEN NULL ELSE PostStatus END,
                        PostError = CASE WHEN ISNULL(PostStatus, '') = 'Failed' THEN NULL ELSE PostError END
                    WHERE StagingId = @stagingId
                      AND ISNULL(PostStatus, '') <> 'Posted'
                    """;
                await context.Database.ExecuteSqlRawAsync(
                    validSql,
                    new object[]
                    {
                        new SqlParameter("@billingId", resolvedCust),
                        new SqlParameter("@notes", TruncateNotes(notes)),
                        new SqlParameter("@stagingId", stagingId)
                    },
                    cancellationToken);
                return LockboxBillingIdUpdateResult.Ok(
                    $"Billing ID saved as {resolvedCust}. Status set to VALID — eligible for the next GP poster run.",
                    becameValid: true);
            }

            var invalidNotes =
                $"Billing ID \"{trimmed}\" was not found in RM00101. Corrected by {trimmedUser} at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.";
            const string invalidSql = """
                UPDATE dbo.C_LB_Payment_Staging
                SET BillingId = @billingId,
                    ValidationStatus = 'INVALID_CUSTOMER',
                    ValidationNotes = @notes
                WHERE StagingId = @stagingId
                  AND ISNULL(PostStatus, '') <> 'Posted'
                """;
            await context.Database.ExecuteSqlRawAsync(
                invalidSql,
                new object[]
                {
                    new SqlParameter("@billingId", trimmed),
                    new SqlParameter("@notes", TruncateNotes(invalidNotes)),
                    new SqlParameter("@stagingId", stagingId)
                },
                cancellationToken);
            return LockboxBillingIdUpdateResult.Ok(
                $"Billing ID saved, but \"{trimmed}\" was not found in Great Plains (RM00101). Status remains INVALID_CUSTOMER.",
                becameValid: false);
        }

        private (string WhereClause, object[] Parameters) BuildWhereClause(
            IntegrationType integration,
            DateTime? fromDate,
            DateTime? toDate,
            string? validationStatus,
            string? fileName)
        {
            var lab = _options.GetLab(integration);
            // BofA files store zero-padded lockbox (e.g. 0000744833); compare trimmed numeric form.
            var sb = new StringBuilder("""
                WHERE TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) IS NOT NULL
                  AND CAST(TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) AS VARCHAR(20)) = @lockboxId
                """);

            var parameters = new List<object>
            {
                new SqlParameter("@lockboxId", lab.LockboxId)
            };

            if (fromDate.HasValue)
            {
                sb.AppendLine("  AND DepositDate >= @fromDate");
                parameters.Add(new SqlParameter("@fromDate", fromDate.Value.Date));
            }

            if (toDate.HasValue)
            {
                sb.AppendLine("  AND DepositDate <= @toDate");
                parameters.Add(new SqlParameter("@toDate", toDate.Value.Date));
            }

            if (!string.IsNullOrWhiteSpace(validationStatus))
            {
                sb.AppendLine("  AND ValidationStatus = @validationStatus");
                parameters.Add(new SqlParameter("@validationStatus", validationStatus.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                sb.AppendLine("  AND FileName LIKE @fileNamePattern");
                parameters.Add(new SqlParameter("@fileNamePattern", $"%{fileName.Trim()}%"));
            }

            return (sb.ToString(), parameters.ToArray());
        }

        private (string WhereClause, object[] Parameters) BuildPostHistoryWhereClause(
            IntegrationType integration,
            DateTime? postedFrom,
            DateTime? postedTo,
            string? postStatus,
            string? billingId,
            string? documentNumber,
            string? fileName)
        {
            var lab = _options.GetLab(integration);
            var sb = new StringBuilder("""
                WHERE TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) IS NOT NULL
                  AND CAST(TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) AS VARCHAR(20)) = @lockboxId
                  AND PostStatus IN ('Posted', 'Failed')
                """);

            var parameters = new List<object>
            {
                new SqlParameter("@lockboxId", lab.LockboxId)
            };

            var status = (postStatus ?? "").Trim();
            if (status.Equals("Posted", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("  AND PostStatus = @postStatus");
                parameters.Add(new SqlParameter("@postStatus", status));
            }

            if (postedFrom.HasValue)
            {
                sb.AppendLine("  AND PostedAt >= @postedFrom");
                parameters.Add(new SqlParameter("@postedFrom", postedFrom.Value.Date));
            }

            if (postedTo.HasValue)
            {
                // Inclusive end date: keep rows through end of selected calendar day.
                sb.AppendLine("  AND PostedAt < @postedToExclusive");
                parameters.Add(new SqlParameter("@postedToExclusive", postedTo.Value.Date.AddDays(1)));
            }

            if (!string.IsNullOrWhiteSpace(billingId))
            {
                sb.AppendLine("  AND BillingId LIKE @billingIdPattern");
                parameters.Add(new SqlParameter("@billingIdPattern", $"%{billingId.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(documentNumber))
            {
                sb.AppendLine("  AND ExternalDocNumber LIKE @docPattern");
                parameters.Add(new SqlParameter("@docPattern", $"%{documentNumber.Trim()}%"));
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                sb.AppendLine("  AND FileName LIKE @fileNamePattern");
                parameters.Add(new SqlParameter("@fileNamePattern", $"%{fileName.Trim()}%"));
            }

            return (sb.ToString(), parameters.ToArray());
        }

        private (string WhereClause, object[] Parameters) BuildCheckValidationWhereClause(
            IntegrationType integration,
            DateTime? fromDate,
            DateTime? toDate,
            string? statusFilter,
            string? billingId,
            bool exceptionsOnly)
        {
            var lab = _options.GetLab(integration);
            var sb = new StringBuilder("""
                WHERE TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) IS NOT NULL
                  AND CAST(TRY_CAST(LTRIM(RTRIM(LockboxNumber)) AS BIGINT) AS VARCHAR(20)) = @lockboxId
                  AND ISNULL(PostStatus, '') <> 'Posted'
                """);

            var parameters = new List<object>
            {
                new SqlParameter("@lockboxId", lab.LockboxId)
            };

            // Check Validations is an exception queue only — never VALID/PENDING.
            if (exceptionsOnly || string.IsNullOrWhiteSpace(statusFilter))
            {
                sb.AppendLine("  AND ValidationStatus IN ('INVALID_CUSTOMER', 'WARN', 'ERROR')");
            }
            else
            {
                var status = statusFilter.Trim();
                if (status.Equals("INVALID_CUSTOMER", StringComparison.OrdinalIgnoreCase))
                {
                    // Invalid customer card/filter includes WARN (same work queue as Staging).
                    sb.AppendLine("  AND ValidationStatus IN ('INVALID_CUSTOMER', 'WARN')");
                }
                else if (status.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                      || status.Equals("WARN", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("  AND ValidationStatus = @validationStatus");
                    parameters.Add(new SqlParameter("@validationStatus", status));
                }
                else
                {
                    // Unknown / VALID / PENDING → keep exception-only (do not show valid payments).
                    sb.AppendLine("  AND ValidationStatus IN ('INVALID_CUSTOMER', 'WARN', 'ERROR')");
                }
            }

            if (fromDate.HasValue)
            {
                sb.AppendLine("  AND DepositDate >= @fromDate");
                parameters.Add(new SqlParameter("@fromDate", fromDate.Value.Date));
            }

            if (toDate.HasValue)
            {
                sb.AppendLine("  AND DepositDate <= @toDate");
                parameters.Add(new SqlParameter("@toDate", toDate.Value.Date));
            }

            if (!string.IsNullOrWhiteSpace(billingId))
            {
                sb.AppendLine("  AND BillingId LIKE @billingIdPattern");
                parameters.Add(new SqlParameter("@billingIdPattern", $"%{billingId.Trim()}%"));
            }

            return (sb.ToString(), parameters.ToArray());
        }

        private static string TruncateNotes(string notes)
        {
            if (notes.Length <= 500)
                return notes;
            return notes.Substring(0, 500);
        }

        private DbContext GetContext(IntegrationType integration) =>
            integration == IntegrationType.EQUINE ? _equineContext : _cahfsContext;

        private sealed class StringScalar
        {
            public string Value { get; set; } = "";
        }
    }
}
