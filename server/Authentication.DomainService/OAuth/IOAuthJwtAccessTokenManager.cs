using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.OAuth
{
    public interface IOAuthJwtAccessTokenManager
    {
        Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, AuthenticationConfiguration authenticationConfiguration, User user, StateInfo? stateInfo = null);
        Task<(string, DateTime)> ManageRefreshTokenAsync(TokenRequest tokenRequest, JwtAccessToken jwtAccessToken, AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user);
    }
}