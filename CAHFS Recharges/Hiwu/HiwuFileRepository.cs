using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuFileRepository : IHiwuFileRepository
    {
        private readonly CahfsIntegrationsContext _db;

        public HiwuFileRepository(CahfsIntegrationsContext db)
        {
            _db = db;
        }

        public Task<HiwuImportFile?> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
        {
            return _db.ImportFiles.AsNoTracking()
                .FirstOrDefaultAsync(f => f.ContentHash == contentHash, cancellationToken);
        }

        public Task<List<string>> ListRemoteFileNamesAsync(CancellationToken cancellationToken = default)
        {
            return _db.ImportFiles.AsNoTracking()
                .Select(f => f.RemoteFileName)
                .ToListAsync(cancellationToken);
        }

        public Task<List<HiwuImportFile>> ListStoredFilesAsync(CancellationToken cancellationToken = default)
        {
            return _db.ImportFiles
                .Where(f => f.ParseStatus == HiwuParseStatus.Stored)
                .ToListAsync(cancellationToken);
        }

        public Task<List<HiwuImportFile>> ListUnlinkedAmendmentsAsync(CancellationToken cancellationToken = default)
        {
            return _db.ImportFiles
                .Where(f => f.IsAmendment && f.OriginalFileId == null)
                .ToListAsync(cancellationToken);
        }

        public Task<List<HiwuImportFile>> ListByNamePrefixAsync(string namePrefix, CancellationToken cancellationToken = default)
        {
            return _db.ImportFiles.AsNoTracking()
                .Where(f => !f.IsAmendment && f.RemoteFileName.StartsWith(namePrefix))
                .ToListAsync(cancellationToken);
        }

        public Task AddFileAsync(HiwuImportFile file, CancellationToken cancellationToken = default)
        {
            return _db.ImportFiles.AddAsync(file, cancellationToken).AsTask();
        }

        public void Detach(HiwuImportFile file)
        {
            _db.Entry(file).State = EntityState.Detached;
        }

        public async Task ReplaceImportSamplesAsync(
            Guid fileId,
            IReadOnlyList<string> sampleIds,
            CancellationToken cancellationToken = default)
        {
            var existing = await _db.ImportSamples.Where(row => row.FileId == fileId).ToListAsync(cancellationToken);
            if (existing.Count > 0)
                _db.ImportSamples.RemoveRange(existing);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sampleId in sampleIds)
            {
                var id = sampleId.Trim();
                if (id.Length == 0 || id.Length > 100 || !seen.Add(id))
                    continue;
                _db.ImportSamples.Add(new HiwuImportSample { FileId = fileId, SampleId = id });
            }
        }

        public async Task BackfillSampleIndexAsync(CancellationToken cancellationToken = default)
        {
            // Prefer files that look like they have samples but SampleCount was never set
            // (amended/received already in DB before SampleCount shipped). Avoid brittle "<sample"
            // string match — namespaced XML often uses prefixes.
            // Also evaluate HasBSample once for legacy rows (NULL = not checked yet).
            // B-Sample is not on C_HIWU_Cofl_Sample — only the manifest / Import_File flag.
            var files = await _db.ImportFiles
                .Where(file => file.SampleCount == 0 || file.HasBSample == null)
                .OrderByDescending(file => file.ReceivedUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            var updated = 0;
            foreach (var file in files)
            {
                if (!HiwuManifestDocument.TryRead(file.RawXml, out var document))
                {
                    if (file.HasBSample == null)
                    {
                        file.HasBSample = false;
                        updated++;
                    }
                    continue;
                }

                var hasBSample = HiwuManifestDocument.AnyBSampleRequested(document);
                if (document.Samples.Count > 0 && file.SampleCount == 0)
                {
                    var sampleIds = new List<string>(document.Samples.Count);
                    for (var index = 0; index < document.Samples.Count; index++)
                        sampleIds.Add(HiwuCoflFileOrderService.SampleKey(document.Samples[index], index));

                    file.SampleCount = document.Samples.Count;
                    await ReplaceImportSamplesAsync(file.FileId, sampleIds, cancellationToken);
                }

                if (file.HasBSample != hasBSample)
                    file.HasBSample = hasBSample;
                updated++;
            }

            if (updated > 0)
                await _db.SaveChangesAsync(cancellationToken);
        }

        public Task AddIngestRunAsync(HiwuIngestRun run, CancellationToken cancellationToken = default)
        {
            return _db.IngestRuns.AddAsync(run, cancellationToken).AsTask();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _db.SaveChangesAsync(cancellationToken);
        }
    }
}
