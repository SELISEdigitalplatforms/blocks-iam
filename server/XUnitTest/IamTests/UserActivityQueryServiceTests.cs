using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Activity.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests
{
    public class UserActivityQueryServiceTests : IDisposable
    {
        private readonly Mock<IUserActivityRepository> _repo = new();

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetContext(string organizationId, string userId)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: userId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: organizationId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        private UserActivityQueryService Create() =>
            new(NullLogger<UserActivityQueryService>.Instance, _repo.Object);

        [Fact]
        public async Task GetActivities_NullFilter_IsInitialized_AndReturnsData()
        {
            SetContext("default", "actor-1");

            GetActivitiesRequest? capturedReq = null;
            var activities = new List<UserActivity>
            {
                new() { ItemId = "a1", UserId = "u9", ActorUserId = "actor-1", Category = UserActivityCategory.Auth, Event = "login" }
            };
            _repo.Setup(r => r.GetAsync("u9", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, GetActivitiesRequest, CancellationToken>((_, r, _) => capturedReq = r)
                .ReturnsAsync(activities);
            _repo.Setup(r => r.CountAsync("u9", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(5);

            var req = new GetActivitiesRequest { Filter = null! };
            var result = await Create().GetActivitiesAsync("u9", req, CancellationToken.None);

            capturedReq!.Filter.Should().NotBeNull();
            result.TotalCount.Should().Be(5);
            result.Data.Should().NotBeNull();
            result.Data.Should().ContainSingle().Which.ItemId.Should().Be("a1");
        }

        [Fact]
        public async Task GetActivities_NonDefaultOrg_OverridesFilterOrganizationId()
        {
            SetContext("org-42", "actor-1");

            GetActivitiesRequest? capturedReq = null;
            _repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, GetActivitiesRequest, CancellationToken>((_, r, _) => capturedReq = r)
                .ReturnsAsync(new List<UserActivity>());
            _repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            await Create().GetActivitiesAsync("u1", new GetActivitiesRequest(), CancellationToken.None);

            capturedReq!.Filter.OrganizationId.Should().Be("org-42");
        }

        [Fact]
        public async Task GetActivities_DefaultOrg_DoesNotOverrideFilterOrganizationId()
        {
            SetContext("default", "actor-1");

            GetActivitiesRequest? capturedReq = null;
            _repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, GetActivitiesRequest, CancellationToken>((_, r, _) => capturedReq = r)
                .ReturnsAsync(new List<UserActivity>());
            _repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            var req = new GetActivitiesRequest { Filter = new GetActivitiesFilter { OrganizationId = "preexisting" } };
            await Create().GetActivitiesAsync("u1", req, CancellationToken.None);

            capturedReq!.Filter.OrganizationId.Should().Be("preexisting");
        }

        [Fact]
        public async Task GetActivities_NoRequestedUserId_FallsBackToCallerUserId()
        {
            SetContext("default", "caller-77");

            string? capturedUserId = null;
            _repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, GetActivitiesRequest, CancellationToken>((u, _, _) => capturedUserId = u)
                .ReturnsAsync(new List<UserActivity>());
            _repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            await Create().GetActivitiesAsync(null, new GetActivitiesRequest(), CancellationToken.None);

            capturedUserId.Should().Be("caller-77");
        }

        [Fact]
        public async Task GetActivities_ExplicitRequestedUserId_IsPreserved()
        {
            SetContext("default", "caller-77");

            string? capturedUserId = null;
            _repo.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, GetActivitiesRequest, CancellationToken>((u, _, _) => capturedUserId = u)
                .ReturnsAsync(new List<UserActivity>());
            _repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            await Create().GetActivitiesAsync("explicit-user", new GetActivitiesRequest(), CancellationToken.None);

            capturedUserId.Should().Be("explicit-user");
        }
    }
}
