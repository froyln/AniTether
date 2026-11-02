using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AniTether.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Anime> Anime => Set<Anime>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<AnimeSourceLink> AnimeSourceLinks => Set<AnimeSourceLink>();
    public DbSet<SourceLinkLookup> SourceLinkLookups => Set<SourceLinkLookup>();
    public DbSet<SourceCheck> SourceChecks => Set<SourceCheck>();
    public DbSet<SourceReport> SourceReports => Set<SourceReport>();
    public DbSet<UserAvatar> UserAvatars => Set<UserAvatar>();
    public DbSet<SyncAccountState> SyncAccountStates => Set<SyncAccountState>();
    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();
    public DbSet<SyncEventReceipt> SyncEventReceipts => Set<SyncEventReceipt>();
    public DbSet<SyncTombstone> SyncTombstones => Set<SyncTombstone>();
    public DbSet<ResumePosition> ResumePositions => Set<ResumePosition>();

    /// <summary>A concurrent request inserted the same key first.</summary>
    public static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Anime>(entity =>
        {
            entity.ToTable("anime");
            entity.HasKey(a => a.AnilistId);
            entity.Property(a => a.AnilistId).ValueGeneratedNever();
        });

        builder.Entity<ListEntry>(entity =>
        {
            entity.ToTable("list_entries");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.AnilistId }).IsUnique();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Revision).HasDefaultValue(0L);

            entity.HasOne(e => e.Anime)
                .WithMany()
                .HasForeignKey(e => e.AnilistId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Source>()
                .WithMany()
                .HasForeignKey(e => e.LastSourceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Source>(entity =>
        {
            entity.ToTable("sources");
            entity.HasKey(s => s.Id);
        });

        builder.Entity<AnimeSourceLink>(entity =>
        {
            entity.ToTable("anime_source_links");
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.SourceId, l.Slug }).IsUnique();
            entity.HasIndex(l => new { l.AnilistId, l.SourceId }).IsUnique();

            entity.HasOne<Anime>()
                .WithMany()
                .HasForeignKey(l => l.AnilistId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Source>()
                .WithMany()
                .HasForeignKey(l => l.SourceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SourceLinkLookup>(entity =>
        {
            entity.ToTable("source_link_lookups");
            entity.HasKey(l => new { l.AnilistId, l.SourceId });

            entity.HasOne<Anime>()
                .WithMany()
                .HasForeignKey(l => l.AnilistId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Source>()
                .WithMany()
                .HasForeignKey(l => l.SourceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SourceCheck>(entity =>
        {
            entity.ToTable("source_checks");
            entity.HasKey(c => c.Id);
            // One current row per (source, kind) — history has no consumer yet.
            entity.HasIndex(c => new { c.SourceId, c.Kind }).IsUnique();

            entity.HasOne<Source>()
                .WithMany()
                .HasForeignKey(c => c.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SourceReport>(entity =>
        {
            entity.ToTable("source_reports");
            entity.HasKey(r => r.Id);

            entity.HasOne<Source>()
                .WithMany()
                .HasForeignKey(r => r.SourceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserAvatar>(entity =>
        {
            entity.ToTable("user_avatars");
            entity.HasKey(a => a.UserId);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SyncAccountState>(entity =>
        {
            entity.ToTable("sync_account_states");
            entity.HasKey(s => s.UserId);
            entity.Property(s => s.HistoryFloor).HasDefaultValue(0L);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SyncChange>(entity =>
        {
            entity.ToTable("sync_changes");
            entity.HasKey(c => new { c.UserId, c.Cursor });
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(c => new { c.UserId, c.CreatedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SyncEventReceipt>(entity =>
        {
            entity.ToTable("sync_event_receipts");
            entity.HasKey(r => new { r.UserId, r.EventId });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SyncTombstone>(entity =>
        {
            entity.ToTable("sync_tombstones");
            entity.HasKey(t => new { t.UserId, t.AnilistId });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Anime>().WithMany().HasForeignKey(t => t.AnilistId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ResumePosition>(entity =>
        {
            entity.ToTable("resume_positions");
            entity.HasKey(r => new { r.UserId, r.AnilistId, r.Episode });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Anime>().WithMany().HasForeignKey(r => r.AnilistId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
