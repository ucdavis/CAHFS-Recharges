using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Data
{
    public class FinancialContext : DbContext
    {
        public virtual DbSet<FeedBatch> FeedBatches { get; set; } = null!;
        public virtual DbSet<FeedItem> FeedItems { get; set; } = null!;
        public virtual DbSet<CoaCorrectionAudit> CoaCorrectionAudits { get; set; } = null!;


        public FinancialContext()
        {

        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (HttpHelper.Settings != null)
            {
                optionsBuilder.UseSqlServer(HttpHelper.GetSetting<string>("ConnectionStrings", "FinancialDb"));
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            FinancialContextConfiguration.ConfigureFeedBatchAndItems(modelBuilder);
            // CoaCorrectionAudit uses data annotations from the model class
        }
    }
}
