using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.DAL.Entities;

public class User
{
    public int ID { get; set; }

    [Required, MaxLength(20)]
    public string Username { get; set; } = string.Empty;
    [Required, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    // Included in the JWT; bumping it invalidates all previously issued tokens
    public int TokenVersion { get; set; }

    [MaxLength(150)]
    public string? Bio { get; set; }
    public string? ProfilePictureUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserFollower> Followers { get; set; } = [];
    public ICollection<UserFollower> Following { get; set; } = [];
}
