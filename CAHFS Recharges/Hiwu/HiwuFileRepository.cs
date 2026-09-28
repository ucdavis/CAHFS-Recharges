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
