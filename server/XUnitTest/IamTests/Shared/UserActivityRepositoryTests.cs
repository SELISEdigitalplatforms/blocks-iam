using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Unit tests for <see cref="UserActivityRepository"/>. The audit collection resolves through the
    /// mocked <see cref="IDbContextProvider"/>; the filter builder branches, sorting and paging inputs
    /// are exercised via <see cref="GetAsync"/>/<see cref="CountAsync"/>.
    /// </summary>
    public sealed class UserActivityRepositoryTests : IDisposable
    {
        private readonly Mock<IDbContextProvider> _db = new();

        public UserActivityRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private Mock<IMongoCollection<UserActivity>> Register(IEnumerable<UserActivity>? items = null)
        {
            var col = MongoMock.Collection(items);
            _db.Setup(d => d.GetCollection<UserActivity>(It.IsAny<string>())).Returns(col.Object);
            return col;
        }

        private UserActivityRepository Sut() =>
            new(_db.Object, NullLogger<UserActivityRepository>.Instance);

        private static UserActivity Activity(string id = "a1") =>
            new()
            {
                ItemId = id,
                Category = UserActivityCategory.Auth,
                Event = "login",
                UserId = "u1",
                ActorUserId = "u1"
            };

        [Fact]
        public async Task InsertAsync_InsertsDocument()
        {
            var col = Register();
            await Sut().InsertAsync(Activity(), CancellationToken.None);
            col.Verify(c => c.InsertOneAsync(It.IsAny<UserActivity>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_NoFilter_ReturnsListWithDefaultSort()
        {
            Register(new[] { Activity() });
            var req = new GetActivitiesRequest { Page = 0, PageSize = 20 };
            (await Sut().GetAsync("u1", req, CancellationToken.None)).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetAsync_WithFullFilter_ReturnsList()
        {
            Register(new[] { Activity() });
            var req = new GetActivitiesRequest
            {
                Page = 1,
                PageSize = 10,
                Sort = new BaseSortRequest { Property = "Event", IsDescending = false },
                Filter = new GetActivitiesFilter
                {
                    ActorUserId = "actor-9",
                    Categories = new List<UserActivityCategory> { UserActivityCategory.Auth },
                    Events = new List<string> { "login" },
                    Outcomes = new List<string> { "success" },
                    Severities = new List<string> { "info" },
                    Source = "web",
                    SessionId = "s1",
                    ClientId = "c1",
                    CorrelationId = "corr1",
                    Entity = "user",
                    EntityId = "u1",
                    From = DateTime.UtcNow.AddDays(-7),
                    To = DateTime.UtcNow,
                    Search = "log",
                    OrganizationId = "org1",
                    TenantId = "tenant-9"
                }
            };
            (await Sut().GetAsync("u1", req, CancellationToken.None)).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetAsync_DescendingCustomSort_ReturnsList()
        {
            Register(new[] { Activity() });
            var req = new GetActivitiesRequest
            {
                Sort = new BaseSortRequest { Property = "Severity", IsDescending = true },
                Filter = new GetActivitiesFilter()
            };
            (await Sut().GetAsync("u1", req, CancellationToken.None)).Should().HaveCount(1);
        }

        [Fact]
        public async Task CountAsync_ReturnsCount()
        {
            var col = Register(new[] { Activity(), Activity("a2") });
            MongoMock.SetupCount(col, 2);
            (await Sut().CountAsync("u1", new GetActivitiesRequest(), CancellationToken.None)).Should().Be(2);
        }
    }
}
