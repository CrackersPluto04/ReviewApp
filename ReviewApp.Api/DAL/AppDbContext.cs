using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL;

/// <summary>
/// The EF Core context. The model is mostly attribute-based; <see cref="OnModelCreating"/> holds what attributes
/// cannot express: the media inheritance, composite keys, unique indexes, delete behaviours and the sync columns.
/// </summary>
/// <remarks>
/// Some invariants live in SQL Server triggers created by migrations rather than in C#: <c>SetUpdatedAt</c> on
/// <see cref="Review"/> (maintains the update timestamp) and <c>AddDefaultCollection</c> on <see cref="User"/>
/// (creates the Favourites collection). Delete behaviours are chosen to avoid multiple cascade paths, which SQL
/// Server rejects: anything that would reach the same table twice through <see cref="User"/> is <c>Restrict</c>.
/// </remarks>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Media> Media { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<Music> Musics { get; set; }
    public DbSet<Review> Reviews { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<CollectionMedia> CollectionMedias { get; set; }
    public DbSet<UserFollower> UserFollowers { get; set; }
    public DbSet<ReviewReply> ReviewReplies { get; set; }
    public DbSet<Achievement> Achievements { get; set; }
    public DbSet<UserAchievement> UserAchievements { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<SecurityToken> SecurityTokens { get; set; }
    public DbSet<UserSubscription> UserSubscriptions { get; set; }
    public DbSet<SubscriptionEvent> SubscriptionEvents { get; set; }
    public DbSet<RecognitionUsage> RecognitionUsages { get; set; }
    public DbSet<SyncTombstone> SyncTombstones { get; set; }
    public DbSet<SyncOperation> SyncOperations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Movie, Series and Music share one table, told apart by the MediaType discriminator
        modelBuilder.Entity<Media>()
            .HasDiscriminator(m => m.MediaType)
            .HasValue<Movie>(MediaType.Movie)
            .HasValue<Series>(MediaType.Series)
            .HasValue<Music>(MediaType.Music);

        // One row per source item, so parallel first requests can't store it twice
        modelBuilder.Entity<Media>()
            .HasIndex(m => new { m.MediaType, m.ExternalApiID })
            .IsUnique();

        // Declared so EF knows the table has a trigger and doesn't use OUTPUT clauses that triggers forbid
        modelBuilder.Entity<Review>()
            .ToTable(tb => tb.HasTrigger("SetUpdatedAt"));

        // One review per user and media
        modelBuilder.Entity<Review>()
            .HasIndex(r => new { r.UserID, r.MediaID })
            .IsUnique();

        // The trigger creates the Favourites collection of every new user (declared for the same reason as above)
        modelBuilder.Entity<User>()
            .ToTable(tb => tb.HasTrigger("AddDefaultCollection"));

        // Unique usernames and emails; also what catches two parallel registrations
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Names are unique per user (the column's collation is case-insensitive), Favourites included
        modelBuilder.Entity<Collection>()
            .HasIndex(c => new { c.UserID, c.Name })
            .IsUnique();

        /* Configure CollectionMedia */
        // Composite key
        modelBuilder.Entity<CollectionMedia>()
            .HasKey(cm => new { cm.CollectionID, cm.MediaID });

        // CollectionMedia - Collection relationship
        modelBuilder.Entity<CollectionMedia>()
            .HasOne(cm => cm.Collection)
            .WithMany(c => c.CollectionMedias)
            .HasForeignKey(cm => cm.CollectionID)
            .OnDelete(DeleteBehavior.Cascade);

        // CollectionMedia - Media relationship
        modelBuilder.Entity<CollectionMedia>()
            .HasOne(cm => cm.Media)
            .WithMany()
            .HasForeignKey(cm => cm.MediaID)
            .OnDelete(DeleteBehavior.Cascade);

        /* Configure UserFollower */
        // Composite key
        modelBuilder.Entity<UserFollower>()
            .HasKey(uf => new { uf.FollowerID, uf.FollowingID });

        // Follower relationship
        modelBuilder.Entity<UserFollower>()
            .HasOne(uf => uf.Follower)
            .WithMany(u => u.Following)
            .HasForeignKey(uf => uf.FollowerID)
            .OnDelete(DeleteBehavior.Restrict);

        // Following relationship
        modelBuilder.Entity<UserFollower>()
            .HasOne(uf => uf.Following)
            .WithMany(u => u.Followers)
            .HasForeignKey(uf => uf.FollowingID)
            .OnDelete(DeleteBehavior.Restrict);

        /* Configure ReviewReply */
        // Cascade delete if Review is deleted
        modelBuilder.Entity<ReviewReply>()
            .HasOne(rr => rr.Review)
            .WithMany()
            .HasForeignKey(rr => rr.ReviewID)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT delete on User to prevent multiple cascade paths
        modelBuilder.Entity<ReviewReply>()
            .HasOne(rr => rr.User)
            .WithMany()
            .HasForeignKey(rr => rr.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // RESTRICT delete on ParentReply to avoid self-referencing cycles
        modelBuilder.Entity<ReviewReply>()
            .HasOne(rr => rr.ParentReply)
            .WithMany(rr => rr.ChildReplies)
            .HasForeignKey(rr => rr.ParentReplyID)
            .OnDelete(DeleteBehavior.Restrict);

        /* Configure Achievements */
        // Composite key
        modelBuilder.Entity<UserAchievement>()
            .HasKey(ua => new { ua.UserID, ua.AchievementID });

        // Progress rows are removed together with the user or the achievement definition
        modelBuilder.Entity<UserAchievement>()
            .HasOne(ua => ua.User)
            .WithMany()
            .HasForeignKey(ua => ua.UserID)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserAchievement>()
            .HasOne(ua => ua.Achievement)
            .WithMany()
            .HasForeignKey(ua => ua.AchievementID)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Achievement>()
            .HasIndex(a => a.Code)
            .IsUnique();

        // Tiers of one achievement card share a GroupCode
        modelBuilder.Entity<Achievement>()
            .HasIndex(a => a.GroupCode);

        modelBuilder.Entity<Achievement>()
            .HasData(AchievementSeed.Achievements);

        /* Configure Notifications */
        modelBuilder.Entity<Notification>(entity =>
        {
            // Removed together with the recipient
            entity.HasOne(n => n.RecipientUser)
                  .WithMany()
                  .HasForeignKey(n => n.RecipientUserID)
                  .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT on the actor to prevent multiple cascade paths from User
            entity.HasOne(n => n.ActorUser)
                  .WithMany()
                  .HasForeignKey(n => n.ActorUserID)
                  .OnDelete(DeleteBehavior.Restrict);

            // Listing (by recipient, newest first, optionally by seen state) and the unseen count
            entity.HasIndex(n => new { n.RecipientUserID, n.IsSeen, n.ID });
        });

        /* Configure SecurityTokens */
        modelBuilder.Entity<SecurityToken>(entity =>
        {
            entity.HasKey(t => t.ID);

            entity.HasIndex(t => new { t.UserID, t.Type, t.IsUsed });

            // Link tokens are looked up by their hash
            entity.HasIndex(t => t.TokenHash);

            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserID)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        /* Configure Subscriptions */
        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasOne(s => s.User)
                  .WithMany()
                  .HasForeignKey(s => s.UserID)
                  .OnDelete(DeleteBehavior.Cascade);

            // One record per user, resubscribing reuses it
            entity.HasIndex(s => s.UserID).IsUnique();

            // Provider notifications are matched by these (the subscription ID index is filtered to non-null values)
            entity.HasIndex(s => s.ProviderCustomerID).IsUnique();
            entity.HasIndex(s => s.ProviderSubscriptionID).IsUnique();

            // Expiry job: running subscriptions whose period is over
            entity.HasIndex(s => new { s.Status, s.CurrentPeriodEnd });

            // Every change sets UpdatedAt, so two concurrent updates (e.g. two webhook deliveries) can't both
            // apply the same state change and notify twice: the second one fails and retries on fresh data
            entity.Property(s => s.UpdatedAt).IsConcurrencyToken();
        });

        modelBuilder.Entity<SubscriptionEvent>()
            .HasIndex(e => e.ProviderEventID)
            .IsUnique();

        /* Configure camera recognition usage (daily allowance) */
        modelBuilder.Entity<RecognitionUsage>(entity =>
        {
            entity.HasOne(u => u.User)
                  .WithMany()
                  .HasForeignKey(u => u.UserID)
                  .OnDelete(DeleteBehavior.Cascade);

            // Counting a user's recognitions since the start of the day
            entity.HasIndex(u => new { u.UserID, u.CreatedAt });
        });

        /* Configure offline sync */
        // RowVersion is a SQL Server rowversion (the sync's base version and changes-feed cursor), read as a long.
        // Not IsRowVersion(): that also makes it a concurrency token, and concurrent web edits must keep "last write wins".
        modelBuilder.Entity<Review>(entity =>
        {
            RowVersionColumn(entity.Property(r => r.RowVersion));
            entity.HasIndex(r => new { r.UserID, r.RowVersion });
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            RowVersionColumn(entity.Property(c => c.RowVersion));
            entity.Property(c => c.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.HasIndex(c => new { c.UserID, c.RowVersion });
        });

        modelBuilder.Entity<SyncTombstone>(entity =>
        {
            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserID)
                  .OnDelete(DeleteBehavior.Cascade);

            RowVersionColumn(entity.Property(t => t.RowVersion));
            entity.HasIndex(t => new { t.UserID, t.RowVersion });
            entity.HasIndex(t => t.DeletedAt);
        });

        modelBuilder.Entity<SyncOperation>(entity =>
        {
            entity.HasOne(o => o.User)
                  .WithMany()
                  .HasForeignKey(o => o.UserID)
                  .OnDelete(DeleteBehavior.Cascade);

            // A change is applied at most once per user
            entity.HasIndex(o => new { o.UserID, o.OperationID }).IsUnique();
            // One created entity per client reference
            entity.HasIndex(o => new { o.UserID, o.ClientRef })
                  .IsUnique()
                  .HasFilter("[ClientRef] IS NOT NULL");
            entity.HasIndex(o => o.CreatedAt);
        });
    }

    // Maps a rowversion column (set by SQL Server on every write, read back after each save) to a long without
    // making it a concurrency token
    private static void RowVersionColumn(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<long> property) =>
        property.HasColumnType("rowversion")
                .HasConversion<byte[]>()
                .ValueGeneratedOnAddOrUpdate();
}
