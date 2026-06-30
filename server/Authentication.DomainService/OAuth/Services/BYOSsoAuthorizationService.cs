using Blocks.Genesis;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth.Services
{
    public class BYOSsoAuthorizationService : SocialAuthorizationServiceBase
    {
        public BYOSsoAuthorizationService(
            ILogger<BYOSsoAuthorizationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IUserManagementMutationService userManagementMutationService)
            : base(logger, oAuthJwtAccessTokenManager, oAuthRepository, cacheClient, socialLogInServiceProvider, userManagementMutationService)
        {
        }

        public override async Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var user = await _oAuthRepository.GetUserByEmailAsync(externalUser.Email);

            if (user == null)
            {
                return (null, string.Empty);
            }

            return (user, string.Empty);
        }
    }
}