using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Data
{
    public class FinancialContext : DbContext
    {
        public virtual DbSet<FeedBatch> FeedBatches { get; set; } = null!;
        public virtual DbSet<FeedItem> FeedItems { get; set; } = null!;


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
            modelBuilder.Entity<FeedBatch>(entity =>
            {
                entity.ToTable("C_AE_Feed_Batch", "dbo");

                entity.HasKey(e => e.BatchID)
                    .HasName("PK_C_AE_FEED_HEADER");
                entity.Property(e => e.BatchID)
                    .HasColumnName("batchID")
                    .IsRequired();
                entity.Property(e => e.DateSent)
                    .HasColumnName("dateSent")
                    .HasColumnType("datetime");
                entity.Property(e => e.PickupSentFlag)
                    .HasColumnName("pickupSentFlag")
                    .HasColumnType("char(1)")
                    .IsRequired();
                entity.Property(e => e.AEConsumerReferenceID)
                    .HasColumnName("AEConsumerReferenceID")
                    .HasMaxLength(80)
                    .IsRequired();
                entity.Property(e => e.AEConsumnerNotes)
                    .HasColumnName("AEConsumnerNotes")
                    .HasMaxLength(240)
                    .IsRequired();
                entity.Property(e => e.AERequestStatus)
                    .HasColumnName("AERequestStatus")
                    .HasMaxLength(10);
                entity.Property(e => e.ErrorDetail)
                    .HasColumnName("ErrorDetail")
                    .HasMaxLength(500);
                entity.Property(e => e.AEConsumerRequestID)
                    .HasColumnName("AEConsumerRequestID");
                entity.Property(e => e.AETransactionDate)
                    .HasColumnName("AETransactionDate")
                    .HasColumnType("datetime");
                entity.Property(e => e.AEJournalName)
                    .HasColumnName("AEJournalName")
                    .HasMaxLength(100);
                entity.Property(e => e.AEJournalDescription)
                    .HasColumnName("AEJournalDescription")
                    .HasMaxLength(240);
                entity.Property(e => e.AEJournalReference)
                    .HasColumnName("AEJournalReference")
                    .HasMaxLength(25);
                entity.Property(e => e.BatchTotal)
                    .HasColumnName("batchTotal")
                    .HasColumnType("numeric(19, 2)");
            });
        }
    }
}
