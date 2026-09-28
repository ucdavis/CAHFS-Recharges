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
