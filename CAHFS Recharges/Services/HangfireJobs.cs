using CAHFS_Recharges.Hiwu;
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
    /// 4. HiwuDailyIngest (IngestHiwuFilesJob)
    ///    - Schedule: Daily at 3:00 AM Pacific (Hiwu:CronDaily), only when Hiwu:Enabled is true
    ///    - Purpose: Download HIWU manifest files from the HIWU SFTP folder into CAHFS-Integrations
    ///    - Credentials: Credentials:HIWU_SFTP only
    ///
    /// 5. PollPendingAeStatuses (PollPendingAeStatusesJob)
    ///    - Schedule: Every 15 minutes (configurable via Hangfire:CronStatusPoll)
    ///    - Purpose: Ask Aggie Enterprise for the current status of batches already submitted
    ///      that are still Pending, Sent, Submitted, or Processing
    ///    - Scope: CAHFS and EQUINE. Requires AEConsumerRequestID. Does not touch Ready or Needs Review.
    ///    - Concurrency: Disabled (30-minute lock)
    ///
    /// AE journal send is manual via the UI (WednesdaySendLastWeek removed).
    /// 
    /// CONFIGURATION (appsettings.json):
    /// ==================================
    /// - Hangfire:Enabled (bool): Enable/disable Hangfire entirely
    /// - Hangfire:CronDaily (string): Cron expression for daily job
    /// - Hangfire:CronStatusPoll (string): Cron expression for pending-batch status poll
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
        public async Task IngestHiwuFilesJob()
        {
            using var scope = _services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HiwuOptions>>().Value;

            if (!options.Enabled)
            {
                _logger.LogInformation("Hangfire: HIWU ingest skipped (Hiwu:Enabled=false).");
                return;
            }

            var ingest = scope.ServiceProvider.GetRequiredService<HiwuSftpIngestService>();
            _logger.LogInformation("Hangfire: starting HIWU SFTP ingest.");
            await ingest.RunAsync();
            _logger.LogInformation("Hangfire: HIWU SFTP ingest finished.");

            var reports = scope.ServiceProvider.GetRequiredService<HiwuResultsReportService>();
            _logger.LogInformation("Hangfire: storing published HIWU results.");
            await reports.StorePublishedAsync();
            _logger.LogInformation("Hangfire: published HIWU results stored.");
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

        /// Statuses AE (or this app) uses while a submitted journal is still open.
        /// Ready and Needs Review are local pre-send states and are not polled.
        private static readonly string[] InFlightAeStatuses = { "PENDING", "SENT", "SUBMITTED", "PROCESSING", "INPROCESS" };

        [DisableConcurrentExecution(30 * 60)]
        public async Task PollPendingAeStatusesJob()
        {
            using var scope = _services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var dbResolver = scope.ServiceProvider.GetRequiredService<IIntegrationDbResolver>();
            var uploadSvc = scope.ServiceProvider.GetRequiredService<AggieEnterpriseJournalUploadService>();

            var maxBatches = config.GetValue<int?>("Hangfire:MaxBatchesPerRun") ?? 50;

            foreach (var integration in new[] { IntegrationType.CAHFS, IntegrationType.EQUINE })
            {
                var batches = dbResolver.GetFeedBatches(integration);
                var pending = await batches
                    .Where(b =>
                        b.AEConsumerRequestID != null &&
                        b.AERequestStatus != null &&
                        InFlightAeStatuses.Contains(b.AERequestStatus.Trim().ToUpper()))
                    .OrderBy(b => b.DateSent)
                    .Take(maxBatches)
                    .Select(b => new { b.BatchID, b.AERequestStatus })
                    .ToListAsync();

                _logger.LogInformation(
                    "Hangfire: AE status poll {Integration} found {Count} in-flight batch(es).",
                    integration, pending.Count);

                foreach (var batch in pending)
                {
                    try
                    {
                        var statusResult = await uploadSvc.CheckStatusAsync(batch.BatchID, integration);
                        _logger.LogWarning(
                            "Hangfire: AE status poll {Integration} batch {BatchId} was {Previous}: {Message}",
                            integration, batch.BatchID, batch.AERequestStatus, statusResult.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Hangfire: AE status poll failed for batch {BatchId} ({Integration})", batch.BatchID, integration);
                    }
                }
            }
        }
    }
}
