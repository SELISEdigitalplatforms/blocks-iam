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
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ITokenRevocationService _tokenRevocationService;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<LogoutAllWorkerService> _logger;

        public LogoutAllWorkerService(
            ICacheClient cacheClient,
            IRefreshTokenRepository refreshTokenRepository,
            ITokenRevocationService tokenRevocationService,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<LogoutAllWorkerService> logger)
        {
            _cacheClient = cacheClient;
            _refreshTokenRepository = refreshTokenRepository;
            _tokenRevocationService = tokenRevocationService;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }
        public async Task Consume(LogoutAllEvent context)
        {
            var activeTokens = await _refreshTokenRepository.GetActiveTokensByUserAsync(context.UserId);
            var refreshTokens = activeTokens.Select(t => t.TokenId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

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

            await _refreshTokenRepository.RevokeAllByTokenIdsAsync(refreshTokens, "logout_all");

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