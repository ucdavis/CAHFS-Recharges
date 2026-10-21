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

            modelBuilder.Entity<HiwuCoflSampleRow>(entity =>
            {
                entity.ToTable("C_HIWU_Cofl_Sample", "dbo");
                entity.HasKey(e => new { e.FileId, e.SampleKey }).HasName("PK_C_HIWU_Cofl_Sample");
                entity.Property(e => e.SampleKey).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.Property(e => e.ErrorMessage).HasMaxLength(500);
                entity.Property(e => e.UpdatedUtc).HasColumnType("datetime2(3)");
            });

            modelBuilder.Entity<HiwuSampleReview>(entity =>
            {
                entity.ToTable("C_HIWU_Sample_Review", "dbo");
                entity.HasKey(e => new { e.FileId, e.SampleKey }).HasName("PK_C_HIWU_Sample_Review");
                entity.Property(e => e.SampleKey).HasMaxLength(100).IsRequired();
                entity.Property(e => e.ReviewedBy).HasMaxLength(128).IsRequired();
                entity.Property(e => e.ReviewedUtc).HasColumnType("datetime2(3)");
            });

            modelBuilder.Entity<HiwuReportFile>(entity =>
            {
                entity.ToTable("C_HIWU_Report", "dbo");
                entity.HasKey(e => e.FileId).HasName("PK_C_HIWU_Report");
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.Property(e => e.RawJson).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(e => e.ReportXml).HasColumnType("nvarchar(max)");
                entity.Property(e => e.ErrorMessage).HasMaxLength(500);
                entity.Property(e => e.UpdatedUtc).HasColumnType("datetime2(3)");
            });
        }
    }
}
