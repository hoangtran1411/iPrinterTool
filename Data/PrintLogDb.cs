using System.IO;
using Microsoft.EntityFrameworkCore;

namespace iPrinterTool.Data;

public class PrintLogDb : DbContext
{
    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    public static string DbPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iPrinterTool", "printlog.db");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        options.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<JobRecord>().HasIndex(j => new { j.JobNumber, j.Start }).IsUnique();
        b.Entity<JobRecord>().HasIndex(j => j.Start);
    }
}