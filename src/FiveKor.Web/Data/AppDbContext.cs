using Microsoft.EntityFrameworkCore;

namespace FiveKor.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SiteState> States => Set<SiteState>();
    public DbSet<MediaFile> Media => Set<MediaFile>();
    public DbSet<AdminAccount> Admins => Set<AdminAccount>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SiteState>().ToTable("site_state").HasKey(x => x.Id);
        model.Entity<MediaFile>().ToTable("media_files").HasKey(x => x.Id);
        model.Entity<AdminAccount>().ToTable("admin_accounts").HasKey(x => x.Id);
        model.Entity<AdminAccount>().HasIndex(x => x.Email).IsUnique();
    }
}

public sealed class SiteState
{
    public string Id { get; set; } = "main";
    public string Payload { get; set; } = "{}";
    public int Revision { get; set; }
}

public sealed class MediaFile
{
    public string Id { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AdminAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
