using Microsoft.EntityFrameworkCore;
using PrintApp.Models;

namespace PrintApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AstroLabelData> AstroLabelDatas { get; set; }
    public DbSet<PrinterInfo> PrinterInfos { get; set; }
    public DbSet<SVNToastSerialInfo> SVNToastSerialInfos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SVNToastSerialInfo>()
            .HasKey(x => x.SerialNumber);
    }
}