using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuSftpIngestService
    {
        private readonly HiwuOptions _options;
        private readonly HiwuSftpClient _sftpClient;
        private readonly IHiwuFileRepository _repository;
        private readonly HiwuManifestParser _parser;
        private readonly ILogger<HiwuSftpIngestService> _logger;

        public HiwuSftpIngestService(
            IOptions<HiwuOptions> options,
            HiwuSftpClient sftpClient,
            IHiwuFileRepository repository,
            HiwuManifestParser parser,
            ILogger<HiwuSftpIngestService> logger)
        {
            _options = options.Value;
            _sftpClient = sftpClient;
            _repository = repository;
            _parser = parser;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("HIWU ingest skipped (Hiwu:Enabled=false).");
                return;
            }

            await ParseStoredFilesAsync(cancellationToken);

            var run = new HiwuIngestRun
            {
                IngestRunId = Guid.NewGuid(),
                StartedUtc = DateTime.UtcNow,
                Status = HiwuIngestRunStatus.Failed
            };
            await _repository.AddIngestRunAsync(run, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            var folders = Folders();
            var today = LocalDate(DateTime.UtcNow);
            var knownNames = new HashSet<string>(
                await _repository.ListRemoteFileNamesAsync(cancellationToken),
                StringComparer.OrdinalIgnoreCase);
            var start = DownloadStartDate(knownNames, today);
            var manifests = new List<(string Folder, string Name)>();
            var listedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var listFailures = new List<string>();
            var skippedOld = 0;
            var skippedKnown = 0;

            _logger.LogInformation(
                "HIWU ingest downloading files from {Start} through {Today:yyyy-MM-dd}.",
                start?.ToString("yyyy-MM-dd") ?? "the beginning",
                today);

            foreach (var folder in folders)
            {
                try
                {
                    var listing = await _sftpClient.ListFilesAsync(folder, cancellationToken);
                    foreach (var file in listing)
                    {
                        if (file.IsDirectory || !IsManifestName(file.Name))
                            continue;
                        if (!IsInDownloadWindow(file, start, today))
                        {
                            skippedOld++;
                            continue;
                        }

                        if (knownNames.Contains(file.Name))
                        {
                            skippedKnown++;
                            continue;
                        }

                        if (!listedNames.Add(file.Name))
                            continue;

                        manifests.Add((folder, file.Name));
                    }
                }
                catch (Exception ex)
                {
                    listFailures.Add(folder);
                    _logger.LogWarning(ex, "HIWU ingest could not list SFTP path {Path}", folder);
                }
            }

            if (skippedOld > 0)
            {
                _logger.LogInformation(
                    "HIWU ingest skipped {Count} files outside {Start} through {Today:yyyy-MM-dd}.",
                    skippedOld,
                    start?.ToString("yyyy-MM-dd") ?? "the beginning",
                    today);
            }

            if (skippedKnown > 0)
            {
                _logger.LogInformation(
                    "HIWU ingest skipped {Count} files already stored.",
                    skippedKnown);
            }

            if (manifests.Count == 0 && listFailures.Count == folders.Count)
            {
                run.CompletedUtc = DateTime.UtcNow;
                run.Status = HiwuIngestRunStatus.Failed;
                run.ErrorSummary = Trim("Could not list " + string.Join(", ", listFailures));
                await _repository.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(run.ErrorSummary);
            }

            if (manifests.Count == 0)
            {
                run.CompletedUtc = DateTime.UtcNow;
                run.Status = HiwuIngestRunStatus.NoFiles;
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("HIWU ingest found no manifest files in {Paths}", string.Join(", ", folders));
                return;
            }

            var failures = new List<string>();
            foreach (var file in manifests)
            {
                try
                {
                    await StoreFileAsync(run, file.Folder, file.Name, cancellationToken);
                }
                catch (Exception ex)
                {
                    run.FilesFailed++;
                    failures.Add(file.Name);
                    _logger.LogError(ex, "HIWU ingest failed downloading {FileName} from {Path}", file.Name, file.Folder);
                }
            }

            run.CompletedUtc = DateTime.UtcNow;
            run.Status = run.FilesStored == 0 && run.FilesDuplicate == 0 && run.FilesFailed > 0
                ? HiwuIngestRunStatus.Failed
                : HiwuIngestRunStatus.Completed;
            if (failures.Count > 0)
                run.ErrorSummary = Trim(string.Join(", ", failures));
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "HIWU ingest finished. Stored={Stored} Duplicate={Duplicate} Failed={Failed}",
                run.FilesStored,
                run.FilesDuplicate,
                run.FilesFailed);
        }

        private async Task StoreFileAsync(HiwuIngestRun run, string folder, string fileName, CancellationToken cancellationToken)
        {
            var bytes = await _sftpClient.DownloadFileAsync(folder, fileName, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            if (await _repository.FindByContentHashAsync(hash, cancellationToken) != null)
            {
                run.FilesDuplicate++;
                _logger.LogInformation("HIWU ingest skipped duplicate {FileName}", fileName);
                return;
            }

            var xml = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var parsed = _parser.Parse(fileName, xml);
            Guid? originalFileId = null;
            if (parsed.IsAmendment && !string.IsNullOrEmpty(parsed.OriginalNamePrefix))
            {
                var candidates = await _repository.ListByNamePrefixAsync(parsed.OriginalNamePrefix, cancellationToken);
                originalFileId = HiwuFileName.ChooseEarliest(candidates, parsed.OriginalNamePrefix)?.FileId;
            }

            var file = new HiwuImportFile
            {
                FileId = Guid.NewGuid(),
                RemoteFileName = fileName,
                ReceivedUtc = DateTime.UtcNow,
                RawXml = xml,
                SizeBytes = bytes.LongLength,
                ContentHash = hash,
                ParseStatus = parsed.Status,
                ParseError = string.IsNullOrEmpty(parsed.ParseError) ? null : Trim(parsed.ParseError),
                IsAmendment = parsed.IsAmendment,
                OriginalFileId = originalFileId
            };

            try
            {
                await _repository.AddFileAsync(file, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                run.FilesStored++;
            }
            catch (DbUpdateException ex) when (IsDuplicateHash(ex))
            {
                _repository.Detach(file);
                run.FilesDuplicate++;
                _logger.LogInformation("HIWU ingest skipped duplicate {FileName}", fileName);
            }
            catch
            {
                _repository.Detach(file);
                throw;
            }
        }

        private async Task ParseStoredFilesAsync(CancellationToken cancellationToken)
        {
            var stored = await _repository.ListStoredFilesAsync(cancellationToken);
            foreach (var file in stored)
            {
                var parsed = _parser.Parse(file.RemoteFileName, file.RawXml);
                file.ParseStatus = parsed.Status;
                file.ParseError = string.IsNullOrEmpty(parsed.ParseError) ? null : Trim(parsed.ParseError);
                file.IsAmendment = parsed.IsAmendment;
            }

            if (stored.Count > 0)
            {
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("HIWU parse updated {Count} stored files.", stored.Count);
            }

            var linked = 0;
            var unlinked = await _repository.ListUnlinkedAmendmentsAsync(cancellationToken);
            foreach (var file in unlinked)
            {
                var name = HiwuFileName.Read(file.RemoteFileName);
                if (string.IsNullOrEmpty(name.OriginalNamePrefix))
                    continue;

                var candidates = await _repository.ListByNamePrefixAsync(name.OriginalNamePrefix, cancellationToken);
                var originalId = HiwuFileName.ChooseEarliest(candidates, name.OriginalNamePrefix)?.FileId;
                if (originalId == null || originalId == file.FileId)
                    continue;

                file.OriginalFileId = originalId;
                linked++;
            }

            if (linked > 0)
            {
                await _repository.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("HIWU parse linked {Count} amendments to their originals.", linked);
            }
        }

        private DateTime? DownloadStartDate(IEnumerable<string> names, DateTime today)
        {
            DateTime? newest = null;
            foreach (var name in names)
            {
                if (!HiwuFileName.TryReadFileDate(name, out var fileDate))
                    continue;
                if (newest == null || fileDate > newest)
                    newest = fileDate;
            }

            var floor = _options.MaxFileAgeDays > 0
                ? today.AddDays(-_options.MaxFileAgeDays)
                : (DateTime?)null;

            if (newest == null)
                return floor;

            if (floor != null && newest < floor)
                return floor;

            return newest;
        }

        private bool IsInDownloadWindow(HiwuRemoteFile file, DateTime? start, DateTime today)
        {
            if (HiwuFileName.TryReadFileDate(file.Name, out var fileDate))
                return (start == null || fileDate >= start.Value) && fileDate <= today;

            if (file.LastWriteTimeUtc is DateTime written)
            {
                var writtenDate = LocalDate(written);
                return (start == null || writtenDate >= start.Value) && writtenDate <= today;
            }

            return false;
        }

        private DateTime LocalDate(DateTime utc)
        {
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HIWU ingest could not resolve time zone {TimeZone}. Using UTC.", _options.TimeZone);
                tz = TimeZoneInfo.Utc;
            }

            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).Date;
        }

        private IReadOnlyList<string> Folders()
        {
            if (_options.RemotePaths == null || _options.RemotePaths.Length == 0)
                return ["/Outgoing", "/Outgoing/processed"];

            return _options.RemotePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsManifestName(string name)
        {
            return name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDuplicateHash(DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return message.Contains("UX_C_HIWU_Import_File_ContentHash", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ContentHash", StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 500)
                return value;
            return value[..500];
        }
    }
}
