using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public class MicrosoftLogInService : SocialLogInServiceBase
    {
        public MicrosoftLogInService(
            ILogger<MicrosoftLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            IHttpService httpService
        ) : base(logger, authenticationRepository, httpService)
        {
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new MicrosoftUserData();
        }
    }
}
