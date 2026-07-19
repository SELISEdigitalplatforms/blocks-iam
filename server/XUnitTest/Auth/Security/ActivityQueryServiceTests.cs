using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Activity.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Moq;

namespace XUnitTest.Auth.Security
{
    public class ActivityQueryServiceTests
    {
        private readonly Mock<IUserActivityQueryService> _inner = new();

        private ActivityQueryService Create() => new(_inner.Object);

        private static UserActivity SampleActivity() => new()
        {
            ItemId = "a1",
            UserId = "u1",
            ActorUserId = "actor-1",
            Category = UserActivityCategory.Auth,
            Event = "login",
            Outcome = "success",
            ReasonCode = "ok",
            Severity = "info",
            Source = "web",
            CorrelationId = "corr-1",
            SessionId = "sess-1",
            ClientId = "app1",
            TenantId = "tenant-1",
            Entity = "user",
            EntityId = "e1",
            Context = new ActivityContext { IpAddress = "1.2.3.4", DeviceName = "MacBook" },
            CreatedDate = new DateTime(2026, 6, 1),
        };

        // ---------- GetActivityPageAsync ----------

        [Fact]
        public async Task GetActivityPage_MapsEntitiesToDtos()
        {
            var response = new BaseQueryListResponse<IQueryable<UserActivity>>
            {
                Data = new List<UserActivity> { SampleActivity() }.AsQueryable(),
                TotalCount = 42,
            };
            _inner.Setup(s => s.GetActivitiesAsync("u1", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var req = new GetActivitiesRequest { Page = 2, PageSize = 25 };
            var result = await Create().GetActivityPageAsync("u1", req, CancellationToken.None);

            result.Page.Should().Be(2);
            result.PageSize.Should().Be(25);
            result.TotalCount.Should().Be(42);
            result.Items.Should().ContainSingle();

            var item = result.Items[0];
            item.ItemId.Should().Be("a1");
            item.UserId.Should().Be("u1");
            item.ActorUserId.Should().Be("actor-1");
            item.Category.Should().Be(UserActivityCategory.Auth);
            item.Event.Should().Be("login");
            item.Outcome.Should().Be("success");
            item.ReasonCode.Should().Be("ok");
            item.Severity.Should().Be("info");
            item.Source.Should().Be("web");
            item.CorrelationId.Should().Be("corr-1");
            item.SessionId.Should().Be("sess-1");
            item.ClientId.Should().Be("app1");
            item.TenantId.Should().Be("tenant-1");
            item.Entity.Should().Be("user");
            item.EntityId.Should().Be("e1");
            item.Context!.IpAddress.Should().Be("1.2.3.4");
            item.CreatedDate.Should().Be(new DateTime(2026, 6, 1));
        }

        [Fact]
        public async Task GetActivityPage_ReturnsEmpty_WhenInnerResponseNull()
        {
            _inner.Setup(s => s.GetActivitiesAsync("u1", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BaseQueryListResponse<IQueryable<UserActivity>>)null!);

            var req = new GetActivitiesRequest { Page = 1, PageSize = 10 };
            var result = await Create().GetActivityPageAsync("u1", req, CancellationToken.None);

            result.Items.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
            result.Page.Should().Be(1);
            result.PageSize.Should().Be(10);
        }

        [Fact]
        public async Task GetActivityPage_ReturnsEmpty_WhenInnerDataNull()
        {
            var response = new BaseQueryListResponse<IQueryable<UserActivity>> { Data = null!, TotalCount = 0 };
            _inner.Setup(s => s.GetActivitiesAsync("u1", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await Create().GetActivityPageAsync("u1", new GetActivitiesRequest(), CancellationToken.None);

            result.Items.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
        }

        // ---------- GetActivitiesForSessionAsync ----------

        [Fact]
        public async Task GetActivitiesForSession_BuildsSessionFilteredRequest_AndReturnsItems()
        {
            GetActivitiesRequest? captured = null;
            var response = new BaseQueryListResponse<IQueryable<UserActivity>>
            {
                Data = new List<UserActivity> { SampleActivity() }.AsQueryable(),
                TotalCount = 1,
            };
            _inner.Setup(s => s.GetActivitiesAsync("u1", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, GetActivitiesRequest, CancellationToken>((_, r, _) => captured = r)
                .ReturnsAsync(response);

            var result = await Create().GetActivitiesForSessionAsync("u1", "sess-1", 3, 15, CancellationToken.None);

            result.Should().ContainSingle();
            result[0].SessionId.Should().Be("sess-1");

            captured.Should().NotBeNull();
            captured!.Page.Should().Be(3);
            captured.PageSize.Should().Be(15);
            captured.Filter.Should().NotBeNull();
            captured.Filter.SessionId.Should().Be("sess-1");
        }

        [Fact]
        public async Task GetActivitiesForSession_ReturnsEmpty_WhenNoActivities()
        {
            var response = new BaseQueryListResponse<IQueryable<UserActivity>>
            {
                Data = new List<UserActivity>().AsQueryable(),
                TotalCount = 0,
            };
            _inner.Setup(s => s.GetActivitiesAsync("u1", It.IsAny<GetActivitiesRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await Create().GetActivitiesForSessionAsync("u1", "sess-x", 0, 50, CancellationToken.None);

            result.Should().BeEmpty();
        }
    }
}
