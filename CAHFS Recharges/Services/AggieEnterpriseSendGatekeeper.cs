using System;
using System.Threading;
using System.Threading.Tasks;
using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Services
{
    public sealed class AggieEnterpriseSendGatekeeper
    {
        private readonly IIntegrationDbResolver _dbResolver;

        public AggieEnterpriseSendGatekeeper(IIntegrationDbResolver dbResolver)
        {
            _dbResolver = dbResolver;
        }

        public sealed record GateResult(bool CanSend, string Message, GateSummary? Summary = null);

        public sealed record GateSummary(
            Guid BatchId,
            string? BatchStatus,
            int TotalItems,
            int PendingCount,
            int InvalidCount,
            int NotValidCount
        );

        public async Task<GateResult> CanSendBatchAsync(Guid batchId, IntegrationType integration, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var feedBatches = _dbResolver.GetFeedBatches(integration);

            var batchStatus = await feedBatches
                .AsNoTracking()
                .Where(b => b.BatchID == batchId)
                .Select(b => b.AERequestStatus)
                .FirstOrDefaultAsync(ct);

            if (batchStatus == null)
                return new GateResult(false, "Batch not found.", new GateSummary(batchId, null, 0, 0, 0, 0));

            var summary = await BuildSummaryAsync(batchId, batchStatus, integration, ct);

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

        private async Task<GateSummary> BuildSummaryAsync(Guid batchId, string? batchStatus, IntegrationType integration, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var feedItems = _dbResolver.GetFeedItems(integration);

            // One query -> all counts
            var agg = await feedItems
                .AsNoTracking()
                .Where(i => i.BatchID == batchId && !i.DoNotInclude)
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
