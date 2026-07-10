using Blocks.Genesis;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Worker
{
    public sealed class LogoutAllWorkerService : IConsumer<LogoutAllEvent>
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITokenRevocationService _tokenRevocationService;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<LogoutAllWorkerService> _logger;

        public LogoutAllWorkerService(
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            ITokenRevocationService tokenRevocationService,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<LogoutAllWorkerService> logger)
        {
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _tokenRevocationService = tokenRevocationService;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }
        public async Task Consume(LogoutAllEvent context)
        {
            var refreshTokens = (await _authenticationRepository.GetActiveIdentitySessionByUserIdAsync(context.UserId)).Select(x => x.RefreshToken).ToList();

            var revokeTasks = refreshTokens.Select(async token =>
            {
                var result = await _tokenRevocationService.RevokeTokenAsync(token, GrantTypes.RefreshToken, string.Empty);
                if (!result.Success)
                {
                    _logger.LogWarning("Refresh-token revocation failed during logout-all: {Error}", result.Error ?? "unknown_error");
                }
                await _cacheClient.RemoveKeyAsync(token);
            });
            await Task.WhenAll(revokeTasks);

            await _authenticationRepository.UpdateSessionStatusForAllRefreshTokenAsync(refreshTokens);

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = context.UserId,
                Category = UserActivityCategory.Auth,
                Event = "LOGGED_OUT_ALL",
                Source = "auth-logout-all",
                Context = new ActivityContext
                {
                    DeviceName = "server"
                }
            });
        }
    }
}