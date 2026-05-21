using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.DAL;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuring Media inheritance
        modelBuilder.Entity<Media>()
            .HasDiscriminator(m => m.MediaType)
            .HasValue<Movie>(MediaType.Movie)
            .HasValue<Series>(MediaType.Series)
            .HasValue<Music>(MediaType.Music);

        // Configure Review trigger
        modelBuilder.Entity<Review>()
            .ToTable(tb => tb.HasTrigger("SetUpdatedAt"));

        // Configure User add-default-collection trigger
        modelBuilder.Entity<User>()
            .ToTable(tb => tb.HasTrigger("AddDefaultCollection"));

        // Configure Collection unique name per user
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
    }
}
