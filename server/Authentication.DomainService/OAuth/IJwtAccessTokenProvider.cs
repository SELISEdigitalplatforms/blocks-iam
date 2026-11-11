using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.OAuth
{
    public interface IJwtAccessTokenProvider
    {
        /// <summary>
        /// Decides the organization an issuance is for, with organization status (spec #668), before anything is
        /// issued. On success it records the decision on <paramref name="tokenRequest"/> and writes the resolved
        /// organization back to <see cref="TokenRequest.OrganizationId"/>, so the refresh token remembers the
        /// organization the token was actually issued for, and returns null. On a refusal it returns the 403/503
        /// response to send and changes nothing. Impersonation issuances resolve exactly as before.
        /// </summary>
        Task<TokenResponse?> ResolveOrganizationAsync(User user, TokenRequest tokenRequest);

        Task<JwtAccessToken> GetJwtAccessToken(
            IdentityConfiguration authenticationConfiguration,
            Tenant tenant,
            User user,
            TokenRequest tokenRequest,
            StateInfo? state = null);
    }
}
