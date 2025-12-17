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

		public DbSet<FeedBatch> FeedBatches { get; set; }
		public DbSet<FeedItem> FeedItems { get; set; }
	}
}
