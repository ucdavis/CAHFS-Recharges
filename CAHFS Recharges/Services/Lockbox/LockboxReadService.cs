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

        private DbContext GetContext(IntegrationType integration) =>
            integration == IntegrationType.EQUINE ? _equineContext : _cahfsContext;
    }
}
