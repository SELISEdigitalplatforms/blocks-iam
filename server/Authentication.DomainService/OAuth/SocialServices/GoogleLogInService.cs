using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public class GoogleLogInService : SocialLogInServiceBase
    {
        public GoogleLogInService(
            ILogger<GoogleLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            IHttpService httpService,
            IIdentityProviderSecretResolver secretResolver
        ) : base(logger, authenticationRepository, httpService, secretResolver)
        {
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new GoogleUserData();
        }
    }
}
