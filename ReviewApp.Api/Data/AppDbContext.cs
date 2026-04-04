using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Models;

namespace ReviewApp.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Media> Media { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<Music> Musics { get; set; }
    public DbSet<Review> Reviews { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuring Media inheritance
        modelBuilder.Entity<Media>()
            .HasDiscriminator(m => m.MediaType)
            .HasValue<Movie>(MediaType.Movie)
            .HasValue<Series>(MediaType.Series)
            .HasValue<Music>(MediaType.Music);
    }
}
