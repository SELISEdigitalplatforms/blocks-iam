using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.OAuth
{
    public interface IJwtAccessTokenProvider
    {
        Task<JwtAccessToken> GetJwtAccessToken(
            AuthenticationConfiguration authenticationConfiguration,
            Tenant tenant,
            User user,
            StateInfo? state = null,
            string? organizationId = null,
            TokenIssuanceContext? issuanceContext = null,
            IEnumerable<string>? clientAllowedScopes = null,
            IEnumerable<string>? clientAllowedServiceAccessResources = null);
    }
}
