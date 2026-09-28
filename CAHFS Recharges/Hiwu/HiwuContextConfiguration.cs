using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Hiwu
{
    public static class HiwuContextConfiguration
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HiwuIngestRun>(entity =>
            {
                entity.ToTable("C_HIWU_Ingest_Run", "dbo");
                entity.HasKey(e => e.IngestRunId).HasName("PK_C_HIWU_Ingest_Run");
                entity.Property(e => e.StartedUtc).HasColumnType("datetime2(3)");
                entity.Property(e => e.CompletedUtc).HasColumnType("datetime2(3)");
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.Property(e => e.ErrorSummary).HasMaxLength(500);
            });

            modelBuilder.Entity<HiwuImportFile>(entity =>
            {
                entity.ToTable("C_HIWU_Import_File", "dbo");
                entity.HasKey(e => e.FileId).HasName("PK_C_HIWU_Import_File");
                entity.Property(e => e.RemoteFileName).HasMaxLength(500).IsRequired();
                entity.Property(e => e.ReceivedUtc).HasColumnType("datetime2(3)");
                entity.Property(e => e.RawXml).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(e => e.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
                entity.Property(e => e.ParseStatus).HasMaxLength(20).IsRequired();
                entity.Property(e => e.ParseError).HasMaxLength(500);

                entity.HasIndex(e => e.ContentHash)
                    .IsUnique()
                    .HasDatabaseName("UX_C_HIWU_Import_File_ContentHash");

                entity.HasOne(e => e.OriginalFile)
                    .WithMany()
                    .HasForeignKey(e => e.OriginalFileId)
                    .OnDelete(DeleteBehavior.NoAction)
                    .HasConstraintName("FK_C_HIWU_Import_File_Original");
            });
        }
    }
}
