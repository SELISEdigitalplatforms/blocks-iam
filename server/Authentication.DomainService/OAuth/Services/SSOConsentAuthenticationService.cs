
using Blocks.Genesis;
using DeviceDetectorNET.Cache;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using System.Text.Json;

namespace Authentication.DomainService.OAuth.Services
{
    public class SSOConsentAuthenticationService : ITokenService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IUserManagementMutationService _userManagementMutationService;

        public SSOConsentAuthenticationService(ICacheClient cacheClient,
                                                IAuthenticationRepository authenticationRepository,
                                                IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
                                                IUserManagementMutationService userManagementMutationService)
        {
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _userManagementMutationService = userManagementMutationService;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            if(!await _cacheClient.KeyExistsAsync(request.Code ))
            {
                return new TokenResponse
                {
                    Error = "invalid_code",
                    ErrorDescription = "The provided authorization code is invalid or has expired."
                };
            }

            var ssoUserData = await _cacheClient.GetStringValueAsync(request.Code);

             var ssoUser = JsonSerializer.Deserialize<CreateUserViaSsoRequest>(ssoUserData);
             if (ssoUser == null) return new TokenResponse { Error = "invalid_request", ErrorDescription = "Invalid SSO session", StatusCode = 400 };
             var result = await _userManagementMutationService.CreateUserViaSsoAsync(ssoUser);
             user = await _authenticationRepository.GetUserByIdAsync(result.ItemId ?? "");

            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
        }
    }
}
