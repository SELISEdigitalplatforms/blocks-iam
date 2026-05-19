using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Dtos;
using Authentication.DomainService.Shared;

namespace Authentication.DomainService.Worker
{
    public class LogoutAllWorkerService : IConsumer<LogoutAllEvent>
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly UnifiedTokenSessionService _unifiedTokenSessionService;

        public LogoutAllWorkerService(
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService,
            UnifiedTokenSessionService unifiedTokenSessionService)
        {
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
            _unifiedTokenSessionService = unifiedTokenSessionService;
        }
        public async Task Consume(LogoutAllEvent context)
        {
            var refreshTokens = (await _authenticationRepository.GetActiveIdentitySessionByUserIdAsync(context.UserId)).Select(x => x.RefreshToken).ToList();
            var revokeTasks = refreshTokens.Select(async x => await _unifiedTokenSessionService.RevokeRefreshToken(x));
            await Task.WhenAll(revokeTasks);

            await _authenticationRepository.UpdateSessionStatusForAllRefreshTokenAsync(refreshTokens);

            await ProcessTimeline(context.UserId);
        }

        public async Task<bool> ProcessTimeline(string userId)
        {
            var eventTimeline = new UserAuthenticationTimelineEvent
            {
                DeviceInformation = new DeviceInformation
                {
                    Device = "server"
                },
                Event = "revoke_access_by_logout_all",
                ActionBy = "call_api_to_logout_all",
                UserId = userId
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, eventTimeline);
            return true;
        }
    }
}
