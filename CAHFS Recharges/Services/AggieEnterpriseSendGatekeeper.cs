using System;
using System.Threading;
using System.Threading.Tasks;
using CAHFS_Recharges.Data;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Services
{
    /// <summary>
    /// Gatekeeper for "Send to Aggie Enterprise" action.
    /// Rules:
    ///  1) Batch must be Ready
    ///  2) No pending validations (NULL DebitStringValid/CreditStringValid)
    ///  3) No invalid COAs
    ///  4) (Strict) every item must have DebitStringValid='Valid' AND CreditStringValid='Valid'
    /// </summary>
    public sealed class AggieEnterpriseSendGatekeeper
    {
        private readonly FinancialContext _db;

        public AggieEnterpriseSendGatekeeper(FinancialContext db)
        {
            _db = db;
        }

        // NOTE: Upload service should use result.CanSend (NOT .Allowed)
        public sealed record GateResult(bool CanSend, string Message, GateSummary? Summary = null);

        public sealed record GateSummary(
            Guid BatchId,
            string? BatchStatus,
            int TotalItems,
            int PendingCount,
            int InvalidCount,
            int NotValidCount
        );

        public async Task<GateResult> CanSendBatchAsync(Guid batchId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Read batch status
            var batchStatus = await _db.FeedBatches
                .AsNoTracking()
                .Where(b => b.BatchID == batchId)
                .Select(b => b.AERequestStatus)
                .FirstOrDefaultAsync(ct);

            if (batchStatus == null)
                return new GateResult(false, "Batch not found.", new GateSummary(batchId, null, 0, 0, 0, 0));

            // Summary counts (single round-trip)
            var summary = await BuildSummaryAsync(batchId, batchStatus, ct);

            // Rule 1: Batch must be Ready
            if (!string.Equals(batchStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                return new GateResult(
                    false,
                    $"Blocked: Batch is not Ready. Current status: {batchStatus}",
                    summary
                );
            }

            // Rule 2: No pending validations
            if (summary.PendingCount > 0)
            {
                return new GateResult(
                    false,
                    $"Blocked: {summary.PendingCount} item(s) are not validated yet. Click 'Validate Pending COA' first.",
                    summary
                );
            }

            // Rule 3: No invalid COAs
            if (summary.InvalidCount > 0)
            {
                return new GateResult(
                    false,
                    $"Blocked: {summary.InvalidCount} item(s) have Invalid COA. Fix/re-validate before sending.",
                    summary
                );
            }

            // Rule 4: Strict not-valid guard (covers any weird non-Valid values)
            if (summary.NotValidCount > 0)
            {
                return new GateResult(
                    false,
                    $"Blocked: {summary.NotValidCount} item(s) are not fully Valid.",
                    summary
                );
            }

            return new GateResult(true, "OK: Batch is Ready to send to Aggie Enterprise.", summary);
        }

        private async Task<GateSummary> BuildSummaryAsync(Guid batchId, string? batchStatus, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // One query -> all counts
            var agg = await _db.FeedItems
                .AsNoTracking()
                .Where(i => i.BatchID == batchId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),

                    Pending = g.Count(x =>
                        x.DebitStringValid == null || x.CreditStringValid == null),

                    Invalid = g.Count(x =>
                        x.DebitStringValid == "Invalid" || x.CreditStringValid == "Invalid"),

                    NotValid = g.Count(x =>
                        x.DebitStringValid != "Valid" || x.CreditStringValid != "Valid")
                })
                .FirstOrDefaultAsync(ct);

            // Batch exists but no items
            if (agg == null)
            {
                return new GateSummary(batchId, batchStatus, 0, 0, 0, 0);
            }

            return new GateSummary(
                batchId,
                batchStatus,
                agg.Total,
                agg.Pending,
                agg.Invalid,
                agg.NotValid
            );
        }
    }
}
