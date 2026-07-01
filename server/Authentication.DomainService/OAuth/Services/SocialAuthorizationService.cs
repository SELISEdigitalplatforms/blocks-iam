using Blocks.Genesis;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public class SocialAuthorizationService : SocialAuthorizationServiceBase
    {
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IUserRepository _userRepository;

        public SocialAuthorizationService(
            ILogger<SocialAuthorizationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IUserManagementMutationService userManagementMutationService,
            IIdentityAccessManagementRepository repository,
            IUserRepository userRepository)
            : base(logger, oAuthJwtAccessTokenManager, oAuthRepository, cacheClient, socialLogInServiceProvider, userManagementMutationService)
        {
            _repository = repository;
            _userRepository = userRepository;
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
