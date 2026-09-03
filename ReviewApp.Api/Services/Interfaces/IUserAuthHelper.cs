namespace ReviewApp.Api.Services.Interfaces;

public interface IUserAuthHelper
{
    int GetSecureUserID();
    int? GetOptionalUserID();
}
