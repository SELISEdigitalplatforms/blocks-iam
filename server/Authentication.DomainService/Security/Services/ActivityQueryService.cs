using Authentication.DomainService.Security.Models;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Activity.Services;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Security.Services
{
    /// <summary>
    /// Thin wrapper that delegates to <c>Iam.DomainService.Activity.Services.IUserActivityQueryService</c>
    /// and materializes <see cref="IQueryable{UserActivity}"/> into wire-shaped
    /// <see cref="ActivityItemDto"/> instances. Keeps the auth self-service API
    /// decoupled from the IAM activity projection.
    /// </summary>
    public sealed class ActivityQueryService : IActivityQueryService
    {
        private readonly Iam.DomainService.Activity.Services.IUserActivityQueryService _userActivityQueryService;

        public ActivityQueryService(IUserActivityQueryService userActivityQueryService)
        {
            _userActivityQueryService = userActivityQueryService;
        }

        public async Task<ActivityPageResponse> GetActivityPageAsync(
            string userId,
            GetActivitiesRequest req,
            CancellationToken ct)
        {
            var response = new ActivityPageResponse
            {
                Page = req.Page,
                PageSize = req.PageSize,
            };

            var inner = await _userActivityQueryService.GetActivitiesAsync(userId, req, ct);
            var items = (inner?.Data ?? new List<UserActivity>().AsQueryable())
                .AsEnumerable()
                .Select(MapToDto)
                .ToList();

            response.Items = items;
            response.TotalCount = inner?.TotalCount ?? 0;
            return response;
        }

        public async Task<IReadOnlyList<ActivityItemDto>> GetActivitiesForSessionAsync(
            string userId,
            string sessionId,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            var req = new GetActivitiesRequest
            {
                Page = page,
                PageSize = pageSize,
                Filter = new GetActivitiesFilter
                {
                    SessionId = sessionId,
                },
            };

            var response = await GetActivityPageAsync(userId, req, ct);
            return response.Items;
        }

        private static ActivityItemDto MapToDto(UserActivity a) => new()
        {
            ItemId = a.ItemId,
            UserId = a.UserId,
            ActorUserId = a.ActorUserId,
            Category = a.Category,
            Event = a.Event,
            Outcome = a.Outcome,
            ReasonCode = a.ReasonCode,
            Severity = a.Severity,
            Source = a.Source,
            CorrelationId = a.CorrelationId,
            SessionId = a.SessionId,
            ClientId = a.ClientId,
            TenantId = a.TenantId,
            Entity = a.Entity,
            EntityId = a.EntityId,
            Context = a.Context,
            CreatedDate = a.CreatedDate,
        };
    }
}
