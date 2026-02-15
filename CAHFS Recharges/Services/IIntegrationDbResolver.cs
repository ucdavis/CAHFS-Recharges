using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Services
{
    /// Resolves the correct DbContext/DbSets based on integration type.
    /// Allows services to work with both CAHFS and EQUINE databases.
    public interface IIntegrationDbResolver
    {
        DbSet<FeedBatch> GetFeedBatches(IntegrationType integration);

        DbSet<FeedItem> GetFeedItems(IntegrationType integration);

        DbSet<CoaCorrectionAudit> GetCoaCorrectionAudits(IntegrationType integration);

        Task<int> SaveChangesAsync(IntegrationType integration, CancellationToken ct = default);

        Task<int> ExecuteSqlAsync(IntegrationType integration, FormattableString sql, CancellationToken ct = default);
    }
}
