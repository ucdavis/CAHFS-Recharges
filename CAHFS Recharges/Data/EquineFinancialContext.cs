using Microsoft.EntityFrameworkCore;
using CAHFS_Recharges.Models;

namespace CAHFS_Recharges.Data
{
	public class EquineFinancialContext : DbContext
	{
		public EquineFinancialContext(DbContextOptions<EquineFinancialContext> options)
			: base(options)
		{
		}

		public DbSet<FeedBatch> FeedBatches { get; set; } = null!;
		public DbSet<FeedItem> FeedItems { get; set; } = null!;
		public DbSet<CoaCorrectionAudit> CoaCorrectionAudits { get; set; } = null!;

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			FinancialContextConfiguration.ConfigureFeedBatchAndItems(modelBuilder);
			// CoaCorrectionAudit uses data annotations from the model class
		}
	}
}
