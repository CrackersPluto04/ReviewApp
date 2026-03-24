using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.Models;

namespace ReviewApp.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; }
}
