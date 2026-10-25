using Iam.DomainService.Notifications;
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Accounts
{
    public class UserActivityWorker : IConsumer<UserActivityEvent>
    {
        private readonly IUserActivityRepository _repo;
        private readonly ILogger<UserActivityWorker> _logger;
        private readonly IBuiltInNotificationSender? _notificationSender;

        public UserActivityWorker(IUserActivityRepository repo, ILogger<UserActivityWorker> logger, IBuiltInNotificationSender? notificationSender = null)
        {
            _repo = repo;
            _logger = logger;
            _notificationSender = notificationSender;
        }

        public async Task Consume(UserActivityEvent e)
        {
            var bc = BlocksContext.GetContext();

            var doc = new UserActivity
            {
                ItemId = Guid.NewGuid().ToString(),
                UserId = e.UserId,
                ActorUserId = string.IsNullOrEmpty(e.ActorUserId) ? bc?.UserId ?? e.UserId : e.ActorUserId,
                Category = e.Category,
                Event = e.Event,
                Outcome = e.Outcome,
                ReasonCode = e.ReasonCode,
                Severity = e.Severity,
                Source = e.Source,
                MessageId = e.MessageId,
                CorrelationId = e.CorrelationId,
                SessionId = e.SessionId,
                ClientId = e.ClientId,
                Context = e.Context,
                Entity = e.Entity,
                EntityId = e.EntityId,
                Metadata = e.Metadata,
                OrganizationId = bc?.OrganizationId,
                TenantId = !string.IsNullOrEmpty(e.TenantId) ? e.TenantId : bc?.TenantId,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = bc?.UserId ?? e.ActorUserId ?? e.UserId
            };

            // Read before the insert, so this login is not counted as its own history.
            var loginHistory = _notificationSender != null && ActivityNotificationRules.NeedsLoginHistory(doc)
                ? await TryGetLoginHistoryAsync(doc)
                : null;

            await _repo.InsertAsync(doc, CancellationToken.None);

            await NotifyAsync(doc, loginHistory);
        }

        private async Task<LoginHistory?> TryGetLoginHistoryAsync(UserActivity doc)
        {
            try
            {
                return await _repo.GetLoginHistoryAsync(doc.UserId!, doc.Event!, doc.Context?.IpAddress, doc.Context?.UserAgent, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Only the type is logged: a message could hold request details.
                _logger.LogWarning("Login history read failed for activity {Event}, error={ErrorType}; no new-login notification", doc.Event, ex.GetType().Name); // NOSONAR S6667 - type only, by design
                return null;
            }
        }

        // Best effort and after the activity is stored: a failure here never fails the message,
        // so the broker does not redeliver it.
        private async Task NotifyAsync(UserActivity doc, LoginHistory? loginHistory)
        {
            if (_notificationSender == null)
            {
                return;
            }

            try
            {
                var notification = ActivityNotificationRules.Build(doc, loginHistory);
                if (notification != null)
                {
                    await _notificationSender.TrySendAsync(notification);
                }
            }
            catch (Exception ex)
            {
                // Only the type is logged: a message could hold request details.
                _logger.LogWarning("Notification for activity {Event} failed, error={ErrorType}", doc.Event, ex.GetType().Name); // NOSONAR S6667 - type only, by design
            }
        }
    }
}