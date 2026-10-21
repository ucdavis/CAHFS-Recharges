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
            var query = Filtered(fromDate, toDate, status);
            return await query
                .OrderByDescending(f => f.ReceivedUtc)
                .Take(1000)
                .ToListAsync(cancellationToken);
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
    }
}
