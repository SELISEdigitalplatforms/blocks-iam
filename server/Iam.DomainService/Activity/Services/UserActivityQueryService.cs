using Blocks.Genesis;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Activity.Services
{
    public class UserActivityQueryService : IUserActivityQueryService
    {

        private readonly ILogger<UserActivityQueryService> _logger;
        private readonly IUserActivityRepository _repository;

        public UserActivityQueryService(
            ILogger<UserActivityQueryService> logger,
            IUserActivityRepository repository)
        {
            _logger = logger;
            _repository = repository;
        }

        public async Task<BaseQueryListResponse<IQueryable<UserActivity>>> GetActivitiesAsync(
            string? requestedUserId,
            GetActivitiesRequest req,
            CancellationToken ct)
        {
            req.Filter ??= new GetActivitiesFilter();

            var context = BlocksContext.GetContext();
            var callerOrgId = context?.OrganizationId;
            var callerUserId = context?.UserId;

            if (!IsDefaultOrg(callerOrgId))
            {
                req.Filter.OrganizationId = callerOrgId;
            }

            if (string.IsNullOrWhiteSpace(requestedUserId)
                && !string.IsNullOrWhiteSpace(callerUserId))
            {
                requestedUserId = callerUserId;
            }

            var items = await _repository.GetAsync(requestedUserId, req, ct);
            var totalCount = await _repository.CountAsync(requestedUserId, req, ct);

            return new BaseQueryListResponse<IQueryable<UserActivity>>
            {
                Data = items.AsQueryable(),
                TotalCount = totalCount
            };
        }

        private static bool IsDefaultOrg(string? organizationId) =>
            string.IsNullOrWhiteSpace(organizationId)
            || string.Equals(organizationId, IdpConstants.DefaultOrganizationId, StringComparison.OrdinalIgnoreCase);

        
    }
}