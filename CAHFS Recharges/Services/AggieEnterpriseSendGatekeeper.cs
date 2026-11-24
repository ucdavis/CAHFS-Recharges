using System;
using System.Threading;
using System.Threading.Tasks;
using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Services
{
    public sealed class AggieEnterpriseSendGatekeeper
    {
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly IConfiguration _configuration;

        public AggieEnterpriseSendGatekeeper(
            IIntegrationDbResolver dbResolver,
            IConfiguration configuration)
        {
            _dbResolver = dbResolver;
            _configuration = configuration;
        }

        public sealed record GateResult(bool CanSend, string Message, GateSummary? Summary = null);

        public sealed record GateSummary(
            Guid BatchId,
            string? BatchStatus,
            int TotalItems,
            int PendingCount,
            int InvalidCount,
            int NotValidCount,
            DateTime? WeekEndDate = null,
            bool WeekInProgress = false
        );

        public async Task<GateResult> CanSendBatchAsync(Guid batchId, IntegrationType integration, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var feedBatches = _dbResolver.GetFeedBatches(integration);

            var batch = await feedBatches
                .AsNoTracking()
                .Where(b => b.BatchID == batchId)
                .Select(b => new
                {
                    b.AERequestStatus,
                    b.WeekEndDate,
                    b.AETransactionDate
                })
                .FirstOrDefaultAsync(ct);

            if (batch == null)
                return new GateResult(false, "Batch not found.", new GateSummary(batchId, null, 0, 0, 0, 0));

            var batchStatus = batch.AERequestStatus;
            var weekEnd = AeBatchSendWindow.ResolveWeekEndDate(batch.WeekEndDate, batch.AETransactionDate);
            var today = AeBatchSendWindow.GetBusinessToday(_configuration);
            var weekInProgress = AeBatchSendWindow.IsWeekInProgress(weekEnd, today);

            var summary = await BuildSummaryAsync(batchId, batchStatus, weekEnd, weekInProgress, integration, ct);

            // Rule 1: Batch must be Ready
            if (!string.Equals(batchStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                return new GateResult(
                    false,
                    $"Blocked: Batch is not Ready. Current status: {batchStatus}",
                    summary
                );
            }

            // Rule 1b: Do not send until after WeekEndDate (month-slice still accumulating)
            if (weekInProgress && weekEnd.HasValue)
            {
                return new GateResult(
                    false,
                    AeBatchSendWindow.WeekInProgressBlockedMessage(weekEnd.Value),
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

        private async Task<GateSummary> BuildSummaryAsync(
            Guid batchId,
            string? batchStatus,
            DateTime? weekEnd,
            bool weekInProgress,
            IntegrationType integration,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var feedItems = _dbResolver.GetFeedItems(integration);

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

            if (agg == null)
            {
                return new GateSummary(batchId, batchStatus, 0, 0, 0, 0, weekEnd, weekInProgress);
            }

            return new GateSummary(
                batchId,
                batchStatus,
                agg.Total,
                agg.Pending,
                agg.Invalid,
                agg.NotValid,
                weekEnd,
                weekInProgress
            );
        }
    }
}
