using CAHFS_Recharges.Models.Lockbox;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Data
{
    /// EF mapping for BofA Lockbox tables (shared by FinancialContext and EquineFinancialContext).
    public static class LockboxContextConfiguration
    {
        public static void ConfigureLockbox(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LockboxIngestRun>(entity =>
            {
                entity.ToTable("C_LB_Ingest_Run", "dbo");
                entity.HasKey(e => e.IngestRunId).HasName("PK_C_LB_Ingest_Run");
                entity.Property(e => e.IngestRunId).HasColumnName("IngestRunId");
                entity.Property(e => e.LockboxId).HasColumnName("LockboxId").HasMaxLength(10).IsRequired();
                entity.Property(e => e.TargetBusinessDate).HasColumnName("TargetBusinessDate").HasColumnType("date");
                entity.Property(e => e.StartedAtUtc).HasColumnName("StartedAtUtc").HasColumnType("datetime2(3)");
                entity.Property(e => e.CompletedAtUtc).HasColumnName("CompletedAtUtc").HasColumnType("datetime2(3)");
                entity.Property(e => e.Status).HasColumnName("Status").HasMaxLength(20).IsRequired();
                entity.Property(e => e.FilesFound).HasColumnName("FilesFound");
                entity.Property(e => e.FilesDownloaded).HasColumnName("FilesDownloaded");
                entity.Property(e => e.FilesSkipped).HasColumnName("FilesSkipped");
                entity.Property(e => e.ErrorSummary).HasColumnName("ErrorSummary");
            });

            modelBuilder.Entity<LockboxFile>(entity =>
            {
                entity.ToTable("C_LB_Lockbox_File", "dbo");
                entity.HasKey(e => e.FileId).HasName("PK_C_LB_Lockbox_File");
                entity.Property(e => e.FileId).HasColumnName("FileId");
                entity.Property(e => e.LockboxId).HasColumnName("LockboxId").HasMaxLength(10).IsRequired();
                entity.Property(e => e.RemoteFileName).HasColumnName("RemoteFileName").HasMaxLength(500).IsRequired();
                entity.Property(e => e.FileBusinessDate).HasColumnName("FileBusinessDate").HasColumnType("date");
                entity.Property(e => e.FileSizeBytes).HasColumnName("FileSizeBytes");
                entity.Property(e => e.RemoteLastModifiedUtc).HasColumnName("RemoteLastModifiedUtc").HasColumnType("datetime2(3)");
                entity.Property(e => e.ContentHash).HasColumnName("ContentHash").HasMaxLength(64).IsFixedLength();
                entity.Property(e => e.RawContent).HasColumnName("RawContent").HasColumnType("varbinary(max)");
                entity.Property(e => e.IngestedAtUtc).HasColumnName("IngestedAtUtc").HasColumnType("datetime2(3)");
                entity.Property(e => e.IngestRunId).HasColumnName("IngestRunId");
                entity.Property(e => e.ParseStatus).HasColumnName("ParseStatus").HasMaxLength(20).IsRequired();
                entity.Property(e => e.ParsedAtUtc).HasColumnName("ParsedAtUtc").HasColumnType("datetime2(3)");
                entity.Property(e => e.ParseError).HasColumnName("ParseError").HasMaxLength(500);

                entity.HasOne(e => e.IngestRun)
                    .WithMany(r => r.Files)
                    .HasForeignKey(e => e.IngestRunId)
                    .HasConstraintName("FK_C_LB_Lockbox_File_IngestRun");

                entity.HasIndex(e => e.ContentHash)
                    .IsUnique()
                    .HasDatabaseName("UX_C_LB_Lockbox_File_ContentHash");
            });

            modelBuilder.Entity<LockboxDailyReceipt>(entity =>
            {
                entity.ToTable("C_LB_Daily_Receipt", "dbo");
                entity.HasKey(e => new { e.LockboxId, e.BusinessDate }).HasName("PK_C_LB_Daily_Receipt");
                entity.Property(e => e.LockboxId).HasColumnName("LockboxId").HasMaxLength(10);
                entity.Property(e => e.BusinessDate).HasColumnName("BusinessDate").HasColumnType("date");
                entity.Property(e => e.FileReceived).HasColumnName("FileReceived");
                entity.Property(e => e.LastUpdatedUtc).HasColumnName("LastUpdatedUtc").HasColumnType("datetime2(3)");
            });

            modelBuilder.Entity<LockboxAlertState>(entity =>
            {
                entity.ToTable("C_LB_Alert_State", "dbo");
                entity.HasKey(e => e.LockboxId).HasName("PK_C_LB_Alert_State");
                entity.Property(e => e.LockboxId).HasColumnName("LockboxId").HasMaxLength(10);
                entity.Property(e => e.LastAlertSentUtc).HasColumnName("LastAlertSentUtc").HasColumnType("datetime2(3)");
                entity.Property(e => e.LastConsecutiveMissingDays).HasColumnName("LastConsecutiveMissingDays");
            });
        }
    }
}
