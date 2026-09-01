using Blocks.Genesis;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public class SocialAuthorizationService : SocialAuthorizationServiceBase
    {
        private readonly ISsoUserProvisioningService _ssoUserProvisioningService;

        public SocialAuthorizationService(
            ILogger<SocialAuthorizationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            ISsoUserProvisioningService ssoUserProvisioningService)
            : base(logger, oAuthJwtAccessTokenManager, oAuthRepository, cacheClient, socialLogInServiceProvider)
        {
            _ssoUserProvisioningService = ssoUserProvisioningService;
        }

        protected override void NormalizeExternalUserEmail(IExternalUserData externalUser)
        {
            if (string.IsNullOrWhiteSpace(externalUser.Email) && !string.IsNullOrWhiteSpace(externalUser.UserPrincipalName))
            {
                externalUser.Email = externalUser.UserPrincipalName;
            }
        }

        protected override TokenResponse CreateEmailNotProvidedError()
        {
            return new TokenResponse { Error = "External provider did not provide any email or userPrincipalName.", ErrorDescription = "External provider did not provide any email", StatusCode = 401 };
        }

        protected override TokenResponse CreateUserNotFoundError(string userName)
        {
            return new TokenResponse { Error = "user_not_found", ErrorDescription = $"{userName} does not exist", StatusCode = 401 };
        }

        /// <summary>
        /// Resolves the signing-in user, provisioning one when the email is new and the tenant
        /// allows SSO signup. Shared with the OIDC social callback so both behave identically.
        /// A null user still means <c>user_not_found</c> to the caller -- whether the tenant
        /// refused the signup or the write failed is a server-side distinction, not one to
        /// hand an unauthenticated caller.
        /// </summary>
        public override async Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var result = await _ssoUserProvisioningService.ResolveOrProvisionAsync(externalUser, stateInfo.Provider);

            return (result.User, string.Empty);
        }
    }
}
