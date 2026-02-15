using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
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
