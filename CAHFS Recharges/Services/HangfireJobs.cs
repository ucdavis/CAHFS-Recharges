using CAHFS_Recharges.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CAHFS_Recharges.Services
{
    public sealed class HangfireJobs
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<HangfireJobs> _logger;

        public HangfireJobs(IServiceProvider services, ILogger<HangfireJobs> logger)
        {
            _services = services;
            _logger = logger;
        }

        [DisableConcurrentExecution(60 * 60)]
        public async Task ValidatePendingCoasJob()
        {
            using var scope = _services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var validationSvc = scope.ServiceProvider.GetRequiredService<StagingCoaValidationService>();

            var batchSize = config.GetValue<int?>("Hangfire:ValidationBatchSize") ?? 250;
            var maxParallel = config.GetValue<int?>("Hangfire:ValidationMaxParallelCalls") ?? 5;

            _logger.LogInformation(
                "Hangfire: starting COA validation (batchSize={BatchSize}, maxParallel={MaxParallel})",
                batchSize, maxParallel);

            var updated = await validationSvc.ValidatePendingItemsAsync(batchSize, maxParallel);

            _logger.LogInformation("Hangfire: COA validation completed. Items updated: {Updated}", updated);
        }

        [DisableConcurrentExecution(2 * 60 * 60)]
        public async Task SendLastWeekBatchesJob()
        {
            using var scope = _services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<FinancialContext>();
            var gatekeeper = scope.ServiceProvider.GetRequiredService<AggieEnterpriseSendGatekeeper>();
            var uploadSvc = scope.ServiceProvider.GetRequiredService<AggieEnterpriseJournalUploadService>();

            // Resolve "today" in configured timezone (default UTC)
            var tzId = config.GetValue<string>("Hangfire:TimeZone");
            DateTime today = DateTime.UtcNow.Date;

            if (!string.IsNullOrWhiteSpace(tzId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                    today = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz).Date;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Hangfire: failed to resolve timezone '{TimeZone}'. Falling back to UTC.", tzId);
                    today = DateTime.UtcNow.Date;
                }
            }

            // Allow manual runs (so dashboard trigger works any day) - default false
            var allowAnyDay = config.GetValue<bool?>("Hangfire:AllowManualWeeklySend") ?? false;
            if (!allowAnyDay && today.DayOfWeek != DayOfWeek.Wednesday)
            {
                _logger.LogInformation(
                    "Hangfire: skip weekly send (today is {Day}). Set Hangfire:AllowManualWeeklySend=true to allow manual runs.",
                    today.DayOfWeek);
                return;
            }

            // Compute last week Monday..Sunday based on 'today'
            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7; // Monday=0, Sunday=6
            var thisWeekMonday = today.AddDays(-daysSinceMonday);
            var lastWeekMonday = thisWeekMonday.AddDays(-7);
            var lastWeekSunday = lastWeekMonday.AddDays(6);

            var maxBatches = config.GetValue<int?>("Hangfire:MaxBatchesPerRun") ?? 50;

            _logger.LogInformation(
                "Hangfire: weekly send window (Mon..Sun) = {Start:yyyy-MM-dd} .. {End:yyyy-MM-dd}, maxBatches={MaxBatches}, allowAnyDay={AllowAnyDay}",
                lastWeekMonday, lastWeekSunday, maxBatches, allowAnyDay);

            // DateSent is usually NULL until you send, so it won't find "ready to send" batches.
            // Use AETransactionDate (or another business date that exists before sending).
            var candidates = await db.FeedBatches
                .Where(b =>
                    b.AERequestStatus == "Ready" &&
                    b.AETransactionDate != null &&
                    b.AETransactionDate.Value.Date >= lastWeekMonday &&
                    b.AETransactionDate.Value.Date <= lastWeekSunday)
                .OrderBy(b => b.AETransactionDate)
                .Take(maxBatches)
                .Select(b => b.BatchID)
                .ToListAsync();

            _logger.LogInformation("Hangfire: weekly send candidates found = {Count}", candidates.Count);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("Hangfire: no eligible batches found for last week window.");
                return;
            }

            // Poll settings
            var maxAttempts = config.GetValue<int?>("Hangfire:StatusPollMaxAttempts") ?? 8;
            var initialDelaySeconds = config.GetValue<int?>("Hangfire:StatusPollInitialSeconds") ?? 30;
            var maxTotalPollSeconds = config.GetValue<int?>("Hangfire:StatusPollMaxSeconds") ?? 3600;

            foreach (var batchId in candidates)
            {
                try
                {
                    var gate = await gatekeeper.CanSendBatchAsync(batchId);
                    if (!gate.CanSend)
                    {
                        _logger.LogInformation("Hangfire: blocked batch {BatchId}: {Msg}", batchId, gate.Message);
                        continue;
                    }

                    _logger.LogInformation("Hangfire: sending batch {BatchId} to AE...", batchId);

                    var sendResult = await uploadSvc.SendBatchAsync(batchId);
                    if (!sendResult.Success)
                    {
                        _logger.LogWarning("Hangfire: send failed batch {BatchId}: {Msg}", batchId, sendResult.Message);
                        continue;
                    }

                    _logger.LogInformation("Hangfire: send initiated batch {BatchId}. Starting status polling...", batchId);

                    // Poll (exponential backoff) until terminal status OR timeout
                    var attempts = 0;
                    var delay = TimeSpan.FromSeconds(initialDelaySeconds);
                    var startedUtc = DateTime.UtcNow;

                    while (attempts < maxAttempts)
                    {
                        var elapsed = DateTime.UtcNow - startedUtc;
                        if (elapsed.TotalSeconds >= maxTotalPollSeconds)
                        {
                            _logger.LogWarning(
                                "Hangfire: polling timeout batch {BatchId} after {ElapsedSeconds}s (max {MaxSeconds}s).",
                                batchId, (int)elapsed.TotalSeconds, maxTotalPollSeconds);
                            break;
                        }

                        await Task.Delay(delay);
                        attempts++;

                        var statusResult = await uploadSvc.CheckStatusAsync(batchId);
                        _logger.LogInformation(
                            "Hangfire: polled batch {BatchId} attempt {Attempt}/{MaxAttempts}: {Message}",
                            batchId, attempts, maxAttempts, statusResult.Message);

                        var reloaded = await db.FeedBatches.FindAsync(new object[] { batchId });
                        var st = (reloaded?.AERequestStatus ?? string.Empty).ToLowerInvariant();

                        // Terminal states only (DO NOT treat empty as terminal)
                        var isTerminal =
                            st.Contains("processed") ||
                            st.Contains("complete") ||
                            st.Contains("error") ||
                            st.Contains("failed") ||
                            st.Contains("rejected");

                        if (isTerminal)
                        {
                            _logger.LogInformation("Hangfire: terminal status reached batch {BatchId}: {Status}", batchId, reloaded?.AERequestStatus);
                            break;
                        }

                        // exponential backoff, cap individual delay so we remain responsive
                        var nextSeconds = Math.Min(delay.TotalSeconds * 2, 300); // cap per-wait to 5 minutes
                        delay = TimeSpan.FromSeconds(nextSeconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Hangfire: exception processing batch {BatchId}", batchId);
                }
            }
        }
    }
}
