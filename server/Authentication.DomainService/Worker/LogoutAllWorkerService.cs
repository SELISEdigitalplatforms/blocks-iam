using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Worker
{
    /// <summary>
    /// Queued backup for <see cref="LogoutAllEvent"/>: password reset / change end sessions inline
    /// and publish this event only when that fails. Same work, through the same terminator.
    /// </summary>
    public sealed class LogoutAllWorkerService : IConsumer<LogoutAllEvent>
    {
        private readonly IUserSessionTerminator _userSessionTerminator;
        private readonly IUserActivityDispatcher _userActivityDispatcher;

        public LogoutAllWorkerService(
            IUserSessionTerminator userSessionTerminator,
            IUserActivityDispatcher userActivityDispatcher)
        {
            _userSessionTerminator = userSessionTerminator;
            _userActivityDispatcher = userActivityDispatcher;
        }

        public async Task Consume(LogoutAllEvent context)
        {
            await _userSessionTerminator.EndAllSessionsAsync(context.UserId, "logout_all");

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