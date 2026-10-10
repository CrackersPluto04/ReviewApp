namespace ReviewApp.Api.Services.Interfaces;

// Account-level emails. Every method only enqueues, it never throws because of the mail server.
public interface IAccountEmailService
{
    void SendWelcome(string email, string username, string verificationToken);
    void SendVerification(string email, string username, string verificationToken);
    void SendEmailChangeConfirmation(string newEmail, string username, string changeToken);
    void SendEmailChangeRequestedNotice(string oldEmail, string username, string newEmail);
    void SendPasswordChanged(string email, string username);
    void SendTwoFactorCode(string email, string username, string code, TwoFactorCodePurpose purpose);
    void SendTwoFactorStatusChanged(string email, string username, bool enabled);
}

public enum TwoFactorCodePurpose
{
    Login,
    Enable,
    Disable
}
