using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.OAuth
{
    public interface ITokenService
    {
        Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null);
    }
}
