using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Data
{
    /// Shared entity configuration for CAHFS and Equine financial DBs (same schema).
    /// Used by <see cref="FinancialContext"/> and <see cref="EquineFinancialContext"/>.
    public static class FinancialContextConfiguration
    {
        public static void ConfigureFeedBatchAndItems(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FeedBatch>(entity =>
            {
                entity.ToTable("C_AE_Feed_Batch", "dbo");
                entity.HasKey(e => e.BatchID).HasName("PK_C_AE_FEED_HEADER");
                entity.Property(e => e.BatchID).HasColumnName("batchID").IsRequired();
                entity.Property(e => e.DateSent).HasColumnName("dateSent").HasColumnType("datetime");
                entity.Property(e => e.PickupSentFlag).HasColumnName("pickupSentFlag").HasColumnType("char(1)").IsRequired();
                entity.Property(e => e.AEConsumerReferenceID).HasColumnName("AEConsumerReferenceID").HasMaxLength(80).IsRequired();
                entity.Property(e => e.AEConsumnerNotes).HasColumnName("AEConsumnerNotes").HasMaxLength(240).IsRequired();
                entity.Property(e => e.AERequestStatus).HasColumnName("AERequestStatus").HasMaxLength(10);
                entity.Property(e => e.ErrorDetail).HasColumnName("ErrorDetail").HasMaxLength(500);
                entity.Property(e => e.AEConsumerRequestID).HasColumnName("AEConsumerRequestID");
                entity.Property(e => e.AETransactionDate).HasColumnName("AETransactionDate").HasColumnType("datetime");
                entity.Property(e => e.AEJournalName).HasColumnName("AEJournalName").HasMaxLength(100);
                entity.Property(e => e.AEJournalDescription).HasColumnName("AEJournalDescription").HasMaxLength(240);
                entity.Property(e => e.AEJournalReference).HasColumnName("AEJournalReference").HasMaxLength(25);
                entity.Property(e => e.BatchTotal).HasColumnName("batchTotal").HasColumnType("numeric(19, 2)");
            });

            modelBuilder.Entity<FeedItem>(entity =>
            {
                entity.ToTable("C_AE_Feed_Items", "dbo");
                entity.HasKey(e => e.RecordID);
                entity.Property(e => e.RecordID).HasColumnName("recordID").IsRequired();
                entity.Property(e => e.BatchID).HasColumnName("batchID").IsRequired();
                entity.Property(e => e.TransactionDate).HasColumnName("transactionDate").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.OrignalDocNumber).HasColumnName("orginalDocNumber").HasMaxLength(100).IsRequired();
                entity.Property(e => e.ClientID).HasColumnName("clientID").HasMaxLength(50);
                entity.Property(e => e.ClientName).HasColumnName("Client_Name").HasMaxLength(50);
                entity.Property(e => e.OrganizationName).HasColumnName("Organization_Name").HasMaxLength(100);
                entity.Property(e => e.SystemID).HasColumnName("systemID").IsRequired();
                entity.Property(e => e.Quantity).HasColumnName("quantity");
                entity.Property(e => e.UnitPrice).HasColumnName("unitPrice").HasColumnType("numeric(19, 2)");
                entity.Property(e => e.TotalCharge).HasColumnName("totalCharge").HasColumnType("numeric(19, 2)").IsRequired();
                entity.Property(e => e.DebitChartString).HasColumnName("DebitChartString").HasMaxLength(250);
                entity.Property(e => e.CreditChartString).HasColumnName("CreditChartString").HasMaxLength(250);
                entity.Property(e => e.DebitStringValid).HasColumnName("DebitStringValid").HasMaxLength(20);
                entity.Property(e => e.CreditStringValid).HasColumnName("CreditStringValid").HasMaxLength(20);
                entity.Property(e => e.DebitValidationError).HasColumnName("DebitValidationError").HasMaxLength(500);
                entity.Property(e => e.CreditValidationError).HasColumnName("CreditValidationError").HasMaxLength(500);
                entity.Property(e => e.TestCode).HasColumnName("TestCode").HasMaxLength(50);
                entity.Property(e => e.TestName).HasColumnName("TestName").HasMaxLength(200);
                entity.Property(e => e.UnprocessedCOAString).HasColumnName("UnprocessedCOAString").HasMaxLength(500);
            });
        }
    }
}
