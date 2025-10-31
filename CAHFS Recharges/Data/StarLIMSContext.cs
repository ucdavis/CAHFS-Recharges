using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;

namespace CAHFS_Recharges.Data
{
    public class StarLIMSContext : DbContext
    {
        public virtual DbSet<OrdTask> OrdTasks{ get; set; }

        public StarLIMSContext()
        {

        }      

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (HttpHelper.Settings != null)
            {
                optionsBuilder.UseSqlServer(HttpHelper.Settings["ConnectionStrings:StarLIMSDB"]);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrdTask>(entity =>
            {
                entity.ToTable("ORDTASK", "dbo");

                //entity.HasKey(e => e.BatchID)
                //.HasName("PK_C_AE_FEED_HEADER");
                entity.HasNoKey();
                entity.Property(e => e.Ts)
                    .HasColumnName("TS")
                    .IsRequired();
                entity.Property(e => e.Dispsts)
                    .HasColumnName("dispsts");
            });
        }
    }
}
