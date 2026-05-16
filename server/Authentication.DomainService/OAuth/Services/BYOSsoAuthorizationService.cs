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
            IUserManagementMutationService userManagementMutationService,
            IConfiguration configuration)
            : base(logger, oAuthJwtAccessTokenManager, oAuthRepository, cacheClient, socialLogInServiceProvider, userManagementMutationService, configuration)
        {
        }

        public override async Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var user = await _oAuthRepository.GetUserByEmailAsync(externalUser.Email);

            if (user == null)
            {
                // return await CreateUser(stateInfo, externalUser); // for now, we will not auto create user, return error instead. Will add auto create user in the future if needed.   
                return (null, string.Empty);
            }

            return (user, string.Empty);
        }
    }
}