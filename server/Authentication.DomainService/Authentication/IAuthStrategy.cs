using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Facade for the per-grant-type authentication strategies (password, MFA, social).
    /// All three share a uniform <c>AuthenticateAsync(TokenRequest, IdentityConfiguration, ...)</c> contract.
    /// </summary>
    public interface IAuthStrategy
    {
        Task<TokenResponse> AuthenticatePasswordAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration);
        Task<TokenResponse> AuthenticateMfaAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration, User user);
        Task<TokenResponse> AuthenticateSocialAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration);
    }
}
