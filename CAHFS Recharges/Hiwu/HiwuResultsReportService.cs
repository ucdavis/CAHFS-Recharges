using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CAHFS_Recharges.Hiwu
{
    /// Reads finalized (published) TraceFirst results and stores the HIWU report. Does not upload to SFTP.
    public sealed class HiwuResultsReportService
    {
        private readonly CahfsIntegrationsContext _db;
        private readonly HiwuCoflClient _client;
        private readonly ILogger<HiwuResultsReportService> _logger;

        public HiwuResultsReportService(
            CahfsIntegrationsContext db,
            HiwuCoflClient client,
            ILogger<HiwuResultsReportService> logger)
        {
            _db = db;
            _client = client;
            _logger = logger;
        }

        public async Task StorePublishedAsync(CancellationToken cancellationToken = default)
        {
            var imported = await _db.CoflSamples
                .Where(row => row.Status == HiwuCoflSampleStatus.Imported && row.AccessionId != null)
                .ToListAsync(cancellationToken);
            var fileIds = imported.Select(row => row.FileId).Distinct().ToList();
            if (fileIds.Count == 0)
                return;

            var files = await _db.ImportFiles
                .Where(file => fileIds.Contains(file.FileId) && !file.IsAmendment && file.ParseStatus == HiwuParseStatus.Received)
                .ToListAsync(cancellationToken);

            foreach (var file in files)
            {
                var samples = imported.Where(row => row.FileId == file.FileId).ToList();
                var accessionId = samples.Select(row => row.AccessionId).FirstOrDefault(id => id != null);
                if (accessionId == null)
                    continue;

                try
                {
                    var published = await _client.GetPublishedResultsAsync(accessionId.Value, cancellationToken);
                    if (!published.Ok)
                    {
                        _logger.LogWarning(
                            "HIWU published results for accession {AccessionId} failed: {Error}",
                            accessionId,
                            published.Error);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(published.Body) || published.Body.Trim() is "[]" or "{}")
                        continue;

                    var resultsRef = await _client.GetAccessionNumberAsync(accessionId.Value, cancellationToken);
                    var ready = HiwuResultsReportBuilder.TryBuild(published.Body, samples, resultsRef, out var xml, out var error);
                    var row = await _db.Reports.FirstOrDefaultAsync(report => report.FileId == file.FileId, cancellationToken);
                    if (row == null)
                    {
                        row = new HiwuReportFile { FileId = file.FileId };
                        _db.Reports.Add(row);
                    }

                    if (row.Status is HiwuReportStatus.ResultsSent or HiwuReportStatus.ResultsAccepted)
                        continue;

                    row.AccessionId = accessionId.Value;
                    row.RawJson = published.Body;
                    row.ReportXml = ready ? xml : null;
                    row.Status = ready ? HiwuReportStatus.ResultsReady : HiwuReportStatus.ResultsErrored;
                    row.ErrorMessage = error;
                    row.UpdatedUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "HIWU report was not stored for file {FileId}.", file.FileId);
                }
            }
        }

        public async Task<HiwuReportView?> GetViewAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            var row = await _db.Reports.AsNoTracking().FirstOrDefaultAsync(report => report.FileId == fileId, cancellationToken);
            if (row == null)
                return null;
            return HiwuReportView.Read(row);
        }
    }
}
