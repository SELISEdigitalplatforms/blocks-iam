using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Dtos;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Worker
{
    public class LogoutAllWorkerService : IConsumer<LogoutAllEvent>
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ITokenRevocationService _tokenRevocationService;
        private readonly ILogger<LogoutAllWorkerService> _logger;

        public LogoutAllWorkerService(
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService,
            ITokenRevocationService tokenRevocationService,
            ILogger<LogoutAllWorkerService> logger)
        {
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
            _tokenRevocationService = tokenRevocationService;
            _logger = logger;
        }
        public async Task Consume(LogoutAllEvent context)
        {
            var refreshTokens = (await _authenticationRepository.GetActiveIdentitySessionByUserIdAsync(context.UserId)).Select(x => x.RefreshToken).ToList();

            var revokeTasks = refreshTokens.Select(async token =>
            {
                var result = await _tokenRevocationService.RevokeTokenAsync(token, "refresh_token", string.Empty);
                if (!result.Success)
                {
                    _logger.LogWarning("Refresh-token revocation failed during logout-all: {Error}", result.Error ?? "unknown_error");
                }
                await _cacheClient.RemoveKeyAsync(token);
            });
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
