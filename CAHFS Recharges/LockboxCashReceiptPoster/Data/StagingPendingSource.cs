using LockboxCashReceiptPoster.Abstractions;
using LockboxCashReceiptPoster.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace LockboxCashReceiptPoster.Data
{
    public sealed class StagingPendingSource : IPendingReceiptSource
    {
        private readonly string _connectionString;

        public StagingPendingSource(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<IReadOnlyList<CashReceiptRequest>> GetPendingAsync(
            int? maxRows = null,
            CancellationToken cancellationToken = default)
        {
            // BillingId is normalized in C_LB_Build_Staging; view joins RM00101 directly.
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? "TOP (@maxRows) " : "";
            var sql = $@"
SELECT {topClause}
    StagingId,
    FileName,
    BillingId,
    CheckNumber,
    CheckAmount,
    DepositDate,
    BatchNumber
FROM dbo.C_LB_Pending_Cash_Receipts_V
WHERE LEN(LTRIM(RTRIM(ISNULL(BillingId, '')))) > 0
ORDER BY DepositDate, StagingId;";

            var results = new List<CashReceiptRequest>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                if (maxRows.HasValue && maxRows.Value > 0)
                    command.Parameters.AddWithValue("@maxRows", maxRows.Value);

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        results.Add(new CashReceiptRequest
                        {
                            StagingId = reader.GetInt32(0),
                            FileName = reader.GetString(1),
                            CustomerId = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                            CheckNumber = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                            Amount = reader.GetDecimal(4),
                            DepositDate = reader.GetDateTime(5),
                            BatchNumber = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim()
                        });
                    }
                }
            }

            return results;
        }
    }

    public sealed class StagingPostStatusStore : IPostStatusStore
    {
        private readonly string _connectionString;

        public StagingPostStatusStore(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public Task MarkPostedAsync(int stagingId, string? documentNumber, CancellationToken cancellationToken = default)
        {
            const string sql = @"
UPDATE dbo.C_LB_Payment_Staging
SET PostStatus = 'Posted',
    PostedAt = SYSUTCDATETIME(),
    ExternalDocNumber = @doc,
    PostError = NULL
WHERE StagingId = @id
  AND ISNULL(PostStatus, '') <> 'Posted';";

            return ExecuteAsync(sql, stagingId, documentNumber, null, cancellationToken);
        }

        public Task MarkFailedAsync(int stagingId, string errorMessage, CancellationToken cancellationToken = default)
        {
            const string sql = @"
UPDATE dbo.C_LB_Payment_Staging
SET PostStatus = 'Failed',
    PostedAt = SYSUTCDATETIME(),
    PostError = @err
WHERE StagingId = @id
  AND ISNULL(PostStatus, '') <> 'Posted';";

            var trimmed = errorMessage ?? "";
            if (trimmed.Length > 1000)
                trimmed = trimmed.Substring(0, 1000);

            return ExecuteAsync(sql, stagingId, null, trimmed, cancellationToken);
        }

        private async Task ExecuteAsync(
            string sql,
            int stagingId,
            string? documentNumber,
            string? errorMessage,
            CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@id", stagingId);
                if (sql.IndexOf("@doc", StringComparison.Ordinal) >= 0)
                    command.Parameters.AddWithValue("@doc", (object?)documentNumber ?? DBNull.Value);
                if (sql.IndexOf("@err", StringComparison.Ordinal) >= 0)
                    command.Parameters.AddWithValue("@err", (object?)errorMessage ?? DBNull.Value);

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
