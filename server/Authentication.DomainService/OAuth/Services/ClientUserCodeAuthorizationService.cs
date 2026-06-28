using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.OAuth.Services
{
    public class ClientUserCodeAuthorizationService : ITokenService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;

        public ClientUserCodeAuthorizationService(IAuthenticationRepository authenticationRepository, 
                                              IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager)
        {
            _authenticationRepository = authenticationRepository;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null)
        {
            var client = await _authenticationRepository.GetBlocksClientAsync(request.ClientId);

            if (client == null )
            {
                return new TokenResponse { Error = "invalid_client", ErrorDescription = "Client authentication failed" };
            }

            var userCode = await _authenticationRepository.GetUserCodeAsync(request.UserCode);

            if(userCode == null)
            {
                return new TokenResponse { Error = "invalid_user", ErrorDescription = "User authentication failed" };
            }

            user = await _authenticationRepository.GetUserByIdAsync(userCode.UserId);
            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);
        }
    }
}
