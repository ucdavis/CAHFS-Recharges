using System.Security.Cryptography;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Models.Options;
using CAHFS_Recharges.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Services.Lockbox
{
    public sealed class LockboxSftpIngestService
    {
        private readonly LockboxOptions _options;
        private readonly LockboxSftpClient _sftpClient;
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly LockboxMissingFileAlertService _alertService;
        private readonly ILogger<LockboxSftpIngestService> _logger;

        public LockboxSftpIngestService(
            IOptions<LockboxOptions> options,
            LockboxSftpClient sftpClient,
            IIntegrationDbResolver dbResolver,
            LockboxMissingFileAlertService alertService,
            ILogger<LockboxSftpIngestService> logger)
        {
            _options = options.Value;
            _sftpClient = sftpClient;
            _dbResolver = dbResolver;
            _alertService = alertService;
            _logger = logger;
        }

        public async Task RunNightlyIngestAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Lockbox ingest skipped (Lockbox:Enabled=false).");
                return;
            }

            var targetDate = LockboxPacificTime.GetPreviousCalendarDate(_options.TimeZone);
            _logger.LogInformation("Lockbox ingest starting for target business date {TargetDate}", targetDate);

            IReadOnlyList<LockboxRemoteFileInfo> remoteListing;
            try
            {
                remoteListing = await _sftpClient.ListFilesAsync(_options.RemotePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lockbox ingest failed listing SFTP path {Path}", _options.RemotePath);
                throw;
            }

            foreach (var integration in new[] { IntegrationType.CAHFS, IntegrationType.EQUINE })
            {
                try
                {
                    await IngestLabAsync(integration, targetDate, remoteListing, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lockbox ingest failed for integration {Integration}", integration);
                }
            }

            foreach (var integration in new[] { IntegrationType.CAHFS, IntegrationType.EQUINE })
            {
                try
                {
                    await _alertService.CheckAndAlertAsync(integration, targetDate, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lockbox missing-file alert failed for {Integration}", integration);
                }
            }
        }

        private async Task IngestLabAsync(
            IntegrationType integration,
            DateOnly targetDate,
            IReadOnlyList<LockboxRemoteFileInfo> remoteListing,
            CancellationToken cancellationToken)
        {
            var lab = _options.GetLab(integration);
            var runId = Guid.NewGuid();
            var startedUtc = DateTime.UtcNow;

            var ingestRuns = _dbResolver.GetLockboxIngestRuns(integration);
            var run = new LockboxIngestRun
            {
                IngestRunId = runId,
                LockboxId = lab.LockboxId,
                TargetBusinessDate = targetDate,
                StartedAtUtc = startedUtc,
                Status = LockboxIngestRunStatus.Success
            };
            ingestRuns.Add(run);
            await _dbResolver.SaveChangesAsync(integration, cancellationToken);

            var candidates = remoteListing
                .Where(f => !f.IsDirectory)
                .Where(f => LockboxFileNameParser.MatchesTargetDate(f.Name, lab.FileNamePrefix, targetDate))
                .Take(_options.MaxFilesPerRun > 0 ? _options.MaxFilesPerRun : 20)
                .ToList();

            run.FilesFound = candidates.Count;
            var filesDownloaded = 0;
            var filesSkipped = 0;
            string? errorSummary = null;

            try
            {
                if (candidates.Count == 0)
                {
                    run.Status = LockboxIngestRunStatus.NoFiles;
                    await UpsertDailyReceiptAsync(integration, lab.LockboxId, targetDate, fileReceived: false, cancellationToken);
                }
                else
                {
                    var anyReceived = false;
                    foreach (var remote in candidates)
                    {
                        var result = await TryIngestFileAsync(
                            integration, lab, runId, targetDate, remote, cancellationToken);

                        if (result == IngestFileResult.Downloaded)
                        {
                            filesDownloaded++;
                            anyReceived = true;
                        }
                        else if (result == IngestFileResult.SkippedDuplicate)
                        {
                            filesSkipped++;
                            anyReceived = true; // already ingested previously
                        }
                    }

                    run.Status = anyReceived ? LockboxIngestRunStatus.Success : LockboxIngestRunStatus.NoFiles;
                    await UpsertDailyReceiptAsync(
                        integration, lab.LockboxId, targetDate, fileReceived: anyReceived, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                run.Status = LockboxIngestRunStatus.Failed;
                errorSummary = Truncate(ex.Message, 500);
                _logger.LogError(ex, "Lockbox ingest run failed for {LockboxId}", lab.LockboxId);
            }

            run.FilesDownloaded = filesDownloaded;
            run.FilesSkipped = filesSkipped;
            run.ErrorSummary = errorSummary;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _dbResolver.SaveChangesAsync(integration, cancellationToken);

            _logger.LogInformation(
                "Lockbox ingest {Integration} lockbox {LockboxId} date {Date}: status={Status}, found={Found}, downloaded={Downloaded}, skipped={Skipped}",
                integration, lab.LockboxId, targetDate, run.Status, run.FilesFound, filesDownloaded, filesSkipped);
        }

        private enum IngestFileResult
        {
            Downloaded,
            SkippedDuplicate,
            Failed
        }

        private async Task<IngestFileResult> TryIngestFileAsync(
            IntegrationType integration,
            LockboxLabOptions lab,
            Guid ingestRunId,
            DateOnly targetDate,
            LockboxRemoteFileInfo remote,
            CancellationToken cancellationToken)
        {
            byte[] content;
            try
            {
                content = await _sftpClient.DownloadFileAsync(_options.RemotePath, remote.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lockbox failed to download {FileName}", remote.Name);
                return IngestFileResult.Failed;
            }

            var hash = ComputeSha256Hex(content);
            var files = _dbResolver.GetLockboxFiles(integration);
            if (await files.AnyAsync(f => f.ContentHash == hash, cancellationToken))
            {
                _logger.LogInformation("Lockbox skipping duplicate file {FileName} (hash match)", remote.Name);
                return IngestFileResult.SkippedDuplicate;
            }

            var file = new LockboxFile
            {
                FileId = Guid.NewGuid(),
                LockboxId = lab.LockboxId,
                RemoteFileName = remote.Name,
                FileBusinessDate = targetDate,
                FileSizeBytes = content.LongLength,
                RemoteLastModifiedUtc = remote.LastWriteTimeUtc,
                ContentHash = hash,
                RawContent = content,
                IngestedAtUtc = DateTime.UtcNow,
                IngestRunId = ingestRunId,
                ParseStatus = LockboxParseStatus.Pending
            };

            files.Add(file);
            await _dbResolver.SaveChangesAsync(integration, cancellationToken);
            return IngestFileResult.Downloaded;
        }

        private async Task UpsertDailyReceiptAsync(
            IntegrationType integration,
            string lockboxId,
            DateOnly businessDate,
            bool fileReceived,
            CancellationToken cancellationToken)
        {
            var receipts = _dbResolver.GetLockboxDailyReceipts(integration);
            var existing = await receipts
                .FirstOrDefaultAsync(r => r.LockboxId == lockboxId && r.BusinessDate == businessDate, cancellationToken);

            if (existing == null)
            {
                receipts.Add(new LockboxDailyReceipt
                {
                    LockboxId = lockboxId,
                    BusinessDate = businessDate,
                    FileReceived = fileReceived,
                    LastUpdatedUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.FileReceived = fileReceived;
                existing.LastUpdatedUtc = DateTime.UtcNow;
            }

            await _dbResolver.SaveChangesAsync(integration, cancellationToken);
        }

        private static string ComputeSha256Hex(byte[] content)
        {
            var hash = SHA256.HashData(content);
            return Convert.ToHexString(hash);
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];
    }
}
