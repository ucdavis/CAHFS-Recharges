using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Hiwu
{
    /// CAHFS-Integrations database. HIWU history only. 
    public class CahfsIntegrationsContext : DbContext
    {
        public const string ConnectionStringName = "CAHFS-Integrations";

        public CahfsIntegrationsContext(DbContextOptions<CahfsIntegrationsContext> options)
            : base(options)
        {
        }

        public DbSet<HiwuImportFile> ImportFiles { get; set; } = null!;

        public DbSet<HiwuIngestRun> IngestRuns { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            HiwuContextConfiguration.Configure(modelBuilder);
        }
    }
}
