using Blocks.Genesis;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public class MicrosoftLogInService : SocialLogInServiceBase
    {
        public MicrosoftLogInService(
            ILogger<MicrosoftLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        ) : base(logger, authenticationRepository, cacheClient, httpService)
        {
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new MicrosoftUserData();
        }
    }
}
