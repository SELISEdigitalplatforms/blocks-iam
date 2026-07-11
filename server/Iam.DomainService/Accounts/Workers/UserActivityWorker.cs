using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Accounts
{
    public class UserActivityWorker : IConsumer<UserActivityEvent>
    {
        private readonly IUserActivityRepository _repo;
        private readonly ILogger<UserActivityWorker> _logger;

        public UserActivityWorker(IUserActivityRepository repo, ILogger<UserActivityWorker> logger)
        {
            _repo = repo;
            _logger = logger;
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

            await _repo.InsertAsync(doc, CancellationToken.None);
        }
    }
}