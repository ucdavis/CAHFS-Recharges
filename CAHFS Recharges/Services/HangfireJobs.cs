using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Options;
using CAHFS_Recharges.Services.Lockbox;
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
    /// Background jobs managed by Hangfire.
    /// 
    /// SCHEDULED JOBS:
    /// ===============
    /// 
    /// 1. DailyCoaValidation (ValidatePendingCoasJob)
    ///    - Schedule: Daily at 2:00 AM (configurable via Hangfire:CronDaily)
    ///    - Purpose: Validates pending COA chartstrings against Aggie Enterprise API
    ///    - Scope: Processes up to 250 items per run (configurable via Hangfire:ValidationBatchSize)
    ///    - Concurrency: Disabled (only one instance runs at a time, 60-min lock)
    /// 
    /// 2. LockboxDailyIngest (IngestLockboxFilesJob)
    ///    - Schedule: Daily at 3:00 AM (configurable via Lockbox:CronDaily)
    ///    - Purpose: Downloads BofA lockbox files from SFTP for previous calendar day (Pacific)
    ///    - Chains LockboxProcessFiles at end of each run
    ///    - Scope: CAHFS (744833) and EQUINE (744835), separate financial databases
    ///    - Concurrency: Disabled (60-min lock)
    ///
    /// 3. LockboxProcessFiles (ProcessLockboxFilesJob)
    ///    - Schedule: Daily at 3:15 AM (configurable via Lockbox:CronProcess)
    ///    - Purpose: Bulk-load C_LB_Raw_Line and EXEC C_LB_Process_File for Pending/Error files
    ///
    /// 4. WednesdaySendLastWeek (SendLastWeekBatchesJob)
    ///    - Schedule: Wednesdays at 3:00 AM (configurable via Hangfire:CronWednesday)
    ///    - Purpose: Automatically sends Ready batches from the previous week to Aggie Enterprise
    ///    - Scope: Only sends batches where:
    ///      - AERequestStatus is "Ready"
    ///      - AETransactionDate is within last 7 days
    ///      - All items pass gatekeeping (valid COA, no pending validations)
    ///    - Concurrency: Disabled (only one instance runs at a time, 2-hour lock)
    /// 
    /// CONFIGURATION (appsettings.json):
    /// ==================================
    /// - Hangfire:Enabled (bool): Enable/disable Hangfire entirely
    /// - Hangfire:CronDaily (string): Cron expression for daily job
    /// - Hangfire:CronWednesday (string): Cron expression for Wednesday job
    /// - Hangfire:TimeZone (string): Timezone for job scheduling (default: America/Los_Angeles)
    /// - Hangfire:ValidationBatchSize (int): Max items per validation run (default: 250)
    /// - Hangfire:ValidationMaxParallelCalls (int): Parallel API calls (default: 5)
    /// 
    /// ACCESS:
    /// =======
    /// Hangfire Dashboard (/hangfire) is restricted to Admin role only.
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

            var updatedCahfs = await validationSvc.ValidatePendingItemsAsync(IntegrationType.CAHFS, batchSize, maxParallel);
            _logger.LogInformation("Hangfire: COA validation CAHFS completed. Items updated: {Updated}", updatedCahfs);

            var updatedEquine = await validationSvc.ValidatePendingItemsAsync(IntegrationType.EQUINE, batchSize, maxParallel);
            _logger.LogInformation("Hangfire: COA validation EQUINE completed. Items updated: {Updated}", updatedEquine);

            _logger.LogInformation("Hangfire: COA validation completed. CAHFS={Cahfs}, EQUINE={Equine}", updatedCahfs, updatedEquine);
        }

        [DisableConcurrentExecution(60 * 60)]
        public async Task IngestLockboxFilesJob()
        {
            using var scope = _services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LockboxOptions>>().Value;

            if (!options.Enabled)
            {
                _logger.LogInformation("Hangfire: Lockbox ingest skipped (Lockbox:Enabled=false).");
                return;
            }

            var ingest = scope.ServiceProvider.GetRequiredService<LockboxSftpIngestService>();
            _logger.LogInformation("Hangfire: starting Lockbox SFTP ingest.");
            await ingest.RunNightlyIngestAsync();
            _logger.LogInformation("Hangfire: Lockbox SFTP ingest finished.");

            var process = scope.ServiceProvider.GetRequiredService<LockboxFileProcessService>();
            _logger.LogInformation("Hangfire: starting Lockbox parse/staging (post-ingest).");
            await process.ProcessPendingFilesAsync();
            _logger.LogInformation("Hangfire: Lockbox parse/staging finished (post-ingest).");
        }

        [DisableConcurrentExecution(60 * 60)]
        public async Task ProcessLockboxFilesJob()
        {
            using var scope = _services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LockboxOptions>>().Value;

            if (!options.Enabled)
            {
                _logger.LogInformation("Hangfire: Lockbox process skipped (Lockbox:Enabled=false).");
                return;
            }

            var process = scope.ServiceProvider.GetRequiredService<LockboxFileProcessService>();
            _logger.LogInformation("Hangfire: starting Lockbox parse/staging.");
            await process.ProcessPendingFilesAsync();
            _logger.LogInformation("Hangfire: Lockbox parse/staging finished.");
        }

        [DisableConcurrentExecution(2 * 60 * 60)]
        public async Task SendLastWeekBatchesJob()
        {
            using var scope = _services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<FinancialContext>();
            var gatekeeper = scope.ServiceProvider.GetRequiredService<AggieEnterpriseSendGatekeeper>();
            var uploadSvc = scope.ServiceProvider.GetRequiredService<AggieEnterpriseJournalUploadService>();

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

            var allowAnyDay = config.GetValue<bool?>("Hangfire:AllowManualWeeklySend") ?? false;
            if (!allowAnyDay && today.DayOfWeek != DayOfWeek.Wednesday)
            {
                _logger.LogInformation(
                    "Hangfire: skip weekly send (today is {Day}). Set Hangfire:AllowManualWeeklySend=true to allow manual runs.",
                    today.DayOfWeek);
                return;
            }

            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7; // Monday=0, Sunday=6
            var thisWeekMonday = today.AddDays(-daysSinceMonday);
            var lastWeekMonday = thisWeekMonday.AddDays(-7);
            var lastWeekSunday = lastWeekMonday.AddDays(6);

            var maxBatches = config.GetValue<int?>("Hangfire:MaxBatchesPerRun") ?? 50;

            _logger.LogInformation(
                "Hangfire: weekly send window (Mon..Sun) = {Start:yyyy-MM-dd} .. {End:yyyy-MM-dd}, maxBatches={MaxBatches}, allowAnyDay={AllowAnyDay}",
                lastWeekMonday, lastWeekSunday, maxBatches, allowAnyDay);

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

            var maxAttempts = config.GetValue<int?>("Hangfire:StatusPollMaxAttempts") ?? 8;
            var initialDelaySeconds = config.GetValue<int?>("Hangfire:StatusPollInitialSeconds") ?? 30;
            var maxTotalPollSeconds = config.GetValue<int?>("Hangfire:StatusPollMaxSeconds") ?? 3600;

            foreach (var batchId in candidates)
            {
                try
                {
                    var gate = await gatekeeper.CanSendBatchAsync(batchId, IntegrationType.CAHFS);
                    if (!gate.CanSend)
                    {
                        _logger.LogInformation("Hangfire: blocked batch {BatchId}: {Msg}", batchId, gate.Message);
                        continue;
                    }

                    _logger.LogInformation("Hangfire: sending batch {BatchId} to AE...", batchId);

                    var sendResult = await uploadSvc.SendBatchAsync(batchId, IntegrationType.CAHFS);
                    if (!sendResult.Success)
                    {
                        _logger.LogWarning("Hangfire: send failed batch {BatchId}: {Msg}", batchId, sendResult.Message);
                        continue;
                    }

                    _logger.LogInformation("Hangfire: send initiated batch {BatchId}. Starting status polling...", batchId);

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

                        var statusResult = await uploadSvc.CheckStatusAsync(batchId, IntegrationType.CAHFS);
                        _logger.LogInformation(
                            "Hangfire: polled batch {BatchId} attempt {Attempt}/{MaxAttempts}: {Message}",
                            batchId, attempts, maxAttempts, statusResult.Message);

                        var reloaded = await db.FeedBatches.FindAsync(new object[] { batchId });
                        var st = (reloaded?.AERequestStatus ?? string.Empty).ToLowerInvariant();

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

                        var nextSeconds = Math.Min(delay.TotalSeconds * 2, 300); 
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
