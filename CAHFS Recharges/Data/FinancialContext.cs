using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Data
{
    public class FinancialContext : DbContext
    {
        public FinancialContext()
        {

        }

        public virtual DbSet<ExampleTable> AppControls { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (HttpHelper.Settings != null)
            {
                optionsBuilder.UseSqlServer(HttpHelper.Settings["ConnectionStrings:FinancialDb"]);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExampleTable>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.ToTable("TableName");

                entity.Property(e => e.Column)
                    .HasMaxLength(1000)
                    .HasColumnName("my_column");
            });
        }
    }
}
