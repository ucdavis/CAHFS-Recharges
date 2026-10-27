using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuReadService
    {
        private readonly CahfsIntegrationsContext _db;

        public HiwuReadService(CahfsIntegrationsContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<HiwuImportFile>> ListFilesAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? status,
            CancellationToken cancellationToken = default)
        {
            return await ListFilesAsync(fromDate, toDate, status, q: null, cancellationToken);
        }

        public async Task<IReadOnlyList<HiwuImportFile>> ListFilesAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? status,
            string? q,
            CancellationToken cancellationToken = default)
        {
            var query = string.IsNullOrWhiteSpace(q)
                ? Filtered(fromDate, toDate, status)
                : SearchQuery(q.Trim());

            // Anonymous projection — do not project into the mapped entity type (EF can drop fields like SampleCount).
            var rows = await query
                .OrderByDescending(file => file.ReceivedUtc)
                .Take(1000)
                .Select(file => new
                {
                    file.FileId,
                    file.RemoteFileName,
                    file.ReceivedUtc,
                    file.SampleCount,
                    file.SizeBytes,
                    file.ContentHash,
                    file.ParseStatus,
                    file.ParseError,
                    file.IsAmendment,
                    file.OriginalFileId
                })
                .ToListAsync(cancellationToken);

            var list = rows.Select(file => new HiwuImportFile
            {
                FileId = file.FileId,
                RemoteFileName = file.RemoteFileName,
                ReceivedUtc = file.ReceivedUtc,
                RawXml = "",
                SampleCount = file.SampleCount,
                SizeBytes = file.SizeBytes,
                ContentHash = file.ContentHash,
                ParseStatus = file.ParseStatus,
                ParseError = file.ParseError,
                IsAmendment = file.IsAmendment,
                OriginalFileId = file.OriginalFileId
            }).ToList();

            await FillMissingSampleCountsAsync(list, cancellationToken);
            return list;
        }

        public async Task<HiwuImportFile?> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            return await _db.ImportFiles.AsNoTracking()
                .Include(f => f.OriginalFile)
                .FirstOrDefaultAsync(f => f.FileId == fileId, cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, HiwuSampleReview>> ListSampleReviewsAsync(
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            var rows = await _db.SampleReviews.AsNoTracking()
                .Where(row => row.FileId == fileId)
                .ToListAsync(cancellationToken);
            return rows.ToDictionary(row => row.SampleKey, StringComparer.Ordinal);
        }

        public async Task<IReadOnlyDictionary<Guid, int>> CountSampleReviewsAsync(
            IReadOnlyList<Guid> fileIds,
            CancellationToken cancellationToken = default)
        {
            if (fileIds.Count == 0)
                return new Dictionary<Guid, int>();

            return await _db.SampleReviews.AsNoTracking()
                .Where(row => fileIds.Contains(row.FileId))
                .GroupBy(row => row.FileId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
        }

        public async Task MarkSampleReviewedAsync(
            Guid fileId,
            string sampleKey,
            string reviewedBy,
            CancellationToken cancellationToken = default)
        {
            var existing = await _db.SampleReviews
                .AnyAsync(row => row.FileId == fileId && row.SampleKey == sampleKey, cancellationToken);
            if (existing)
                return;

            _db.SampleReviews.Add(new HiwuSampleReview
            {
                FileId = fileId,
                SampleKey = sampleKey,
                ReviewedBy = reviewedBy,
                ReviewedUtc = DateTime.UtcNow
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
            }
        }

        /// When SampleCount was never backfilled, parse RawXml for the current page and persist.
        private async Task FillMissingSampleCountsAsync(
            List<HiwuImportFile> list,
            CancellationToken cancellationToken)
        {
            var missingIds = list.Where(file => file.SampleCount <= 0).Select(file => file.FileId).ToList();
            if (missingIds.Count == 0)
                return;

            var tracked = await _db.ImportFiles
                .Where(file => missingIds.Contains(file.FileId))
                .ToListAsync(cancellationToken);

            var changed = false;
            foreach (var entity in tracked)
            {
                if (!HiwuManifestDocument.TryRead(entity.RawXml, out var document) || document.Samples.Count == 0)
                    continue;

                var count = document.Samples.Count;
                entity.SampleCount = count;
                changed = true;

                var row = list.FirstOrDefault(file => file.FileId == entity.FileId);
                if (row != null)
                    row.SampleCount = count;

                var existing = await _db.ImportSamples
                    .Where(sample => sample.FileId == entity.FileId)
                    .ToListAsync(cancellationToken);
                if (existing.Count > 0)
                    _db.ImportSamples.RemoveRange(existing);

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < document.Samples.Count; index++)
                {
                    var id = HiwuCoflFileOrderService.SampleKey(document.Samples[index], index).Trim();
                    if (id.Length == 0 || id.Length > 100 || !seen.Add(id))
                        continue;
                    _db.ImportSamples.Add(new HiwuImportSample { FileId = entity.FileId, SampleId = id });
                }
            }

            if (changed)
                await _db.SaveChangesAsync(cancellationToken);
        }

        private IQueryable<HiwuImportFile> Filtered(DateTime? fromDate, DateTime? toDate, string? status)
        {
            var query = _db.ImportFiles.AsNoTracking();
            if (fromDate.HasValue)
                query = query.Where(f => f.ReceivedUtc >= fromDate.Value.Date);
            if (toDate.HasValue)
                query = query.Where(f => f.ReceivedUtc < toDate.Value.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(f => f.ParseStatus == status);
            return query;
        }

        /// Global search: ignores From/To. Matches sample id, accession id, or accession number.
        private IQueryable<HiwuImportFile> SearchQuery(string q)
        {
            var needle = q.Trim();
            long? accessionId = long.TryParse(needle, out var id) ? id : null;

            var fromSamples = _db.ImportSamples.AsNoTracking()
                .Where(sample => sample.SampleId == needle)
                .Select(sample => sample.FileId);

            var fromCofl = _db.CoflSamples.AsNoTracking()
                .Where(row =>
                    (accessionId != null && row.AccessionId == accessionId)
                    || (row.AccessionNumber != null && row.AccessionNumber == needle)
                    || row.SampleKey == needle)
                .Select(row => row.FileId);

            var fileIds = fromSamples.Union(fromCofl);
            return _db.ImportFiles.AsNoTracking().Where(file => fileIds.Contains(file.FileId));
        }
    }
}
