namespace CAHFS_Recharges.Hiwu
{
    public interface IHiwuFileRepository
    {
        Task<HiwuImportFile?> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);

        Task<List<string>> ListRemoteFileNamesAsync(CancellationToken cancellationToken = default);

        Task<List<HiwuImportFile>> ListStoredFilesAsync(CancellationToken cancellationToken = default);

        Task<List<HiwuImportFile>> ListUnlinkedAmendmentsAsync(CancellationToken cancellationToken = default);

        Task<List<HiwuImportFile>> ListByNamePrefixAsync(string namePrefix, CancellationToken cancellationToken = default);

        Task AddFileAsync(HiwuImportFile file, CancellationToken cancellationToken = default);

        void Detach(HiwuImportFile file);

        Task AddIngestRunAsync(HiwuIngestRun run, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
