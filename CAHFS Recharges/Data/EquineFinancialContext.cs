using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using Microsoft.EntityFrameworkCore;

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
		public DbSet<LockboxFile> LockboxFiles { get; set; } = null!;
		public DbSet<LockboxIngestRun> LockboxIngestRuns { get; set; } = null!;
		public DbSet<LockboxDailyReceipt> LockboxDailyReceipts { get; set; } = null!;
		public DbSet<LockboxAlertState> LockboxAlertStates { get; set; } = null!;

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			FinancialContextConfiguration.ConfigureFeedBatchAndItems(modelBuilder);
			LockboxContextConfiguration.ConfigureLockbox(modelBuilder);
		}
	}
}
