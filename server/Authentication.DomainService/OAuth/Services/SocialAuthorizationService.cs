using Blocks.Genesis;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;

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

        /// <summary>Required by the abstract base; sign-in itself goes through <see cref="ResolveUserAsync"/>.</summary>
        public override async Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var resolution = await ResolveUserAsync(stateInfo, externalUser);
            return (resolution.User, resolution.RedirectUrl ?? string.Empty);
        }

        /// <summary>
        /// Resolves the signing-in user, provisioning one when the email is new and the tenant allows SSO signup.
        /// Shared with the OIDC social callback so both behave identically. An unavailable auto-join organization
        /// gets its own 400 answer: it is the tenant's configuration failing rather than an unknown user, and a 401
        /// would send the Genesis client back to /login. A refused or failed sign-up still yields a null user, which
        /// the caller reports as <c>user_not_found</c> -- that distinction stays server-side.
        /// </summary>
        protected override async Task<SocialUserResolution> ResolveUserAsync(StateInfo stateInfo, IExternalUserData externalUser)
        {
            ArgumentNullException.ThrowIfNull(stateInfo);

            var result = await _ssoUserProvisioningService.ResolveOrProvisionAsync(externalUser, stateInfo.Provider);

            if (result.Outcome == SsoProvisioningOutcome.OrganizationUnavailable)
            {
                return new SocialUserResolution(null, string.Empty, new TokenResponse
                {
                    Error = IdpConstants.SignupOrganizationUnavailableError,
                    ErrorDescription = IdpConstants.SignupOrganizationUnavailableMessage,
                    StatusCode = 400
                });
            }

            return new SocialUserResolution(result.User, string.Empty);
        }
    }
}
