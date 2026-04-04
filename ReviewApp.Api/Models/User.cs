using System.ComponentModel.DataAnnotations;

namespace ReviewApp.Api.Models;

public class User
{
    public int ID { get; set; }

    [Required, MaxLength(20)]
    public string Username { get; set; } = string.Empty;
    [Required]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Bio { get; set; }
    public string? ProfilePictureUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
