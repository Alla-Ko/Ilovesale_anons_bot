using Announcement.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Announcement.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AnnouncementEntity> Announcements => Set<AnnouncementEntity>();
    public DbSet<CollageEntity> Collages => Set<CollageEntity>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditing();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditing();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditing()
    {
        var utc = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = utc;
                    entry.Entity.UpdatedAtUtc = utc;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = utc;
                    entry.Property(nameof(IAuditable.CreatedAtUtc)).IsModified = false;
                    break;
            }
        }

        TouchAnnouncementWhenCollageChanged(utc);
    }

    private void TouchAnnouncementWhenCollageChanged(DateTime utc)
    {
        foreach (var entry in ChangeTracker.Entries<CollageEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var announcementId = entry.State == EntityState.Deleted
                ? entry.OriginalValues.GetValue<int>(nameof(CollageEntity.AnnouncementId))
                : entry.Entity.AnnouncementId;

            var parentEntry = ChangeTracker.Entries<AnnouncementEntity>()
                .FirstOrDefault(e => e.Entity.Id == announcementId);
            if (parentEntry == null || parentEntry.State == EntityState.Deleted)
                continue;

            parentEntry.Entity.UpdatedAtUtc = utc;
            parentEntry.Property(nameof(AnnouncementEntity.CreatedAtUtc)).IsModified = false;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        });

        builder.Entity<AnnouncementEntity>(e =>
        {
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.UpdatedAtUtc);
            e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
            e.HasOne(x => x.Creator)
                .WithMany()
                .HasForeignKey(x => x.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LastUpdatedBy)
                .WithMany()
                .HasForeignKey(x => x.LastUpdatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CollageEntity>(e =>
        {
            e.Property(x => x.Caption1).HasColumnType("text");
            e.Property(x => x.Caption2).HasColumnType("text");
            e.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
            e.HasOne(x => x.Announcement)
                .WithMany(a => a.Collages)
                .HasForeignKey(x => x.AnnouncementId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
