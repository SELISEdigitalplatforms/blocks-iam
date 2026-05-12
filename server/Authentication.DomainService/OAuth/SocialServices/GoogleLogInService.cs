using Blocks.Genesis;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public class GoogleLogInService : SocialLogInServiceBase
    {
        public GoogleLogInService(
            ILogger<GoogleLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        ) : base(logger, authenticationRepository, cacheClient, httpService)
        {
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new GoogleUserData();
        }
    }
}
