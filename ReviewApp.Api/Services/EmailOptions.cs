using MailKit.Security;

namespace ReviewApp.Api.Services;

/// <summary>
/// SMTP settings, bound from the <c>Email</c> configuration section. The committed defaults point to a local
/// Mailpit (localhost:1025, no authentication); real SMTP credentials only go into the gitignored
/// <c>appsettings.Development.json</c>.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    /// <summary><c>None</c> for Mailpit, <c>StartTls</c> for port 587, <c>SslOnConnect</c> for port 465; <c>Auto</c> picks by port.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.None;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@reviewapp.local";
    public string FromName { get; set; } = "THE Review App";
}
