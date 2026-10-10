using MailKit.Security;

namespace ReviewApp.Api.Services;

// Bound from the "Email" configuration section. The committed defaults point to a local Mailpit
// (localhost:1025, no auth), real SMTP credentials only go into the gitignored appsettings.Development.json.
public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    // None for Mailpit, StartTls for port 587, SslOnConnect for port 465, Auto picks by port
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.None;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@reviewapp.local";
    public string FromName { get; set; } = "THE Review App";
}
