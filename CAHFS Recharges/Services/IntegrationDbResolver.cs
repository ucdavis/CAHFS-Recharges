using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Services
{
    /// Resolves the correct DbContext/DbSets based on integration type.
    public class IntegrationDbResolver : IIntegrationDbResolver
    {
        private readonly FinancialContext _cahfsContext;
        private readonly EquineFinancialContext _equineContext;

        public IntegrationDbResolver(
            FinancialContext cahfsContext,
            EquineFinancialContext equineContext)
        {
            _cahfsContext = cahfsContext;
            _equineContext = equineContext;
        }

        public DbSet<FeedBatch> GetFeedBatches(IntegrationType integration)
        {
            return integration switch
            {
                IntegrationType.EQUINE => _equineContext.FeedBatches,
                IntegrationType.CAHFS => _cahfsContext.FeedBatches,
                _ => _cahfsContext.FeedBatches
            };
        }

        public DbSet<FeedItem> GetFeedItems(IntegrationType integration)
        {
            return integration switch
            {
                IntegrationType.EQUINE => _equineContext.FeedItems,
                IntegrationType.CAHFS => _cahfsContext.FeedItems,
                _ => _cahfsContext.FeedItems
            };
        }

        public DbSet<CoaCorrectionAudit> GetCoaCorrectionAudits(IntegrationType integration)
        {
            return integration switch
            {
                IntegrationType.EQUINE => _equineContext.CoaCorrectionAudits,
                IntegrationType.CAHFS => _cahfsContext.CoaCorrectionAudits,
                _ => _cahfsContext.CoaCorrectionAudits
            };
        }

        public DbSet<LockboxFile> GetLockboxFiles(IntegrationType integration) =>
            integration switch
            {
                IntegrationType.EQUINE => _equineContext.LockboxFiles,
                IntegrationType.CAHFS => _cahfsContext.LockboxFiles,
                _ => _cahfsContext.LockboxFiles
            };

        public DbSet<LockboxIngestRun> GetLockboxIngestRuns(IntegrationType integration) =>
            integration switch
            {
                IntegrationType.EQUINE => _equineContext.LockboxIngestRuns,
                IntegrationType.CAHFS => _cahfsContext.LockboxIngestRuns,
                _ => _cahfsContext.LockboxIngestRuns
            };

        public DbSet<LockboxDailyReceipt> GetLockboxDailyReceipts(IntegrationType integration) =>
            integration switch
            {
                IntegrationType.EQUINE => _equineContext.LockboxDailyReceipts,
                IntegrationType.CAHFS => _cahfsContext.LockboxDailyReceipts,
                _ => _cahfsContext.LockboxDailyReceipts
            };

        public DbSet<LockboxAlertState> GetLockboxAlertStates(IntegrationType integration) =>
            integration switch
            {
                IntegrationType.EQUINE => _equineContext.LockboxAlertStates,
                IntegrationType.CAHFS => _cahfsContext.LockboxAlertStates,
                _ => _cahfsContext.LockboxAlertStates
            };

        public Task<int> SaveChangesAsync(IntegrationType integration, CancellationToken ct = default)
        {
            return integration switch
            {
                IntegrationType.EQUINE => _equineContext.SaveChangesAsync(ct),
                IntegrationType.CAHFS => _cahfsContext.SaveChangesAsync(ct),
                _ => _cahfsContext.SaveChangesAsync(ct)
            };
        }

        public Task<int> ExecuteSqlAsync(IntegrationType integration, FormattableString sql, CancellationToken ct = default)
        {
            return integration switch
            {
                IntegrationType.EQUINE => _equineContext.Database.ExecuteSqlInterpolatedAsync(sql, ct),
                IntegrationType.CAHFS => _cahfsContext.Database.ExecuteSqlInterpolatedAsync(sql, ct),
                _ => _cahfsContext.Database.ExecuteSqlInterpolatedAsync(sql, ct)
            };
        }
    }
}
