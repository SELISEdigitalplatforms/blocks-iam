using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;

namespace Authentication.DomainService.OAuth
{
    public interface IJwtAccessTokenProvider
    {
        Task<JwtAccessToken> GetJwtAccessToken(
            IdentityConfiguration authenticationConfiguration,
            Tenant tenant,
            User user,
            TokenRequest tokenRequest,
            StateInfo? state = null);
    }
}
