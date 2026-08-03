using Authentication.DomainService.Entities;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Dtos;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Security
{
    /// <summary>
    /// Unit tests for <see cref="SecurityRepository"/>. Its three collections resolve from
    /// <see cref="IDbContextProvider.GetDatabase"/>; the refresh-token grouping aggregation, session
    /// projection, rotation history ordering and index creation are all exercised against mocks.
    /// </summary>
    public sealed class SecurityRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _db = new();
        private readonly Mock<IMongoDatabase> _database = MongoMock.Database();

        private SecurityRepository Sut()
        {
            _db.Setup(d => d.GetDatabase()).Returns(_database.Object);
            return new SecurityRepository(_db.Object);
        }

        private Mock<IMongoCollection<RefreshTokenModel>> RegisterRefresh(IEnumerable<RefreshTokenModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, "IdpRefreshTokens", col);
            return col;
        }

        private Mock<IMongoCollection<IdpSessionModel>> RegisterIdp(IEnumerable<IdpSessionModel>? items = null)
        {
            var col = MongoMock.Collection(items);
            MongoMock.OnDatabase(_database, "IdpSessions", col);
            return col;
        }

        private Mock<IMongoCollection<ImpersonationSession>> RegisterImpersonation()
        {
            var col = MongoMock.Collection<ImpersonationSession>();
            MongoMock.OnDatabase(_database, "ImpersonationSessions", col);
            return col;
        }

        private static RefreshTokenModel Token(string session = "s1", string client = "c1", bool revoked = false) =>
            new()
            {
                TokenId = Guid.NewGuid().ToString("n"),
                UserId = "u1",
                TenantId = "tenant1",
                SessionId = session,
                ClientId = client,
                IsRevoked = revoked,
                IssuedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(1),
                SlidingExpiry = DateTime.UtcNow.AddMinutes(30),
                IpAddress = "1.2.3.4",
                DeviceInformation = new DeviceInformation { Device = "Pixel", OS = "Android", Browser = "Chrome" }
            };

        [Fact]
        public async Task GetUserSessionsAsync_EmptyUser_ReturnsEmpty()
        {
            (await Sut().GetUserSessionsAsync("", null, false, CancellationToken.None)).Should().BeEmpty();
        }

        [Fact]
        public async Task GetUserSessionsAsync_GroupsBySession_WithIdpMetadata()
        {
            var refresh = RegisterRefresh();
            MongoMock.SetupAggregate(refresh, new List<RefreshTokenModel>
            {
                Token(session: "s1", client: "app1"),
                Token(session: "s1", client: "app2"),
                Token(session: "s2", client: "app1")
            });
            RegisterIdp(new[]
            {
                new IdpSessionModel { SessionId = "s1", CreatedAt = new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc), LastActivityAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc) }
            });
            RegisterImpersonation();

            var result = await Sut().GetUserSessionsAsync("u1", null, activeOnly: true, CancellationToken.None);

            result.Should().HaveCount(2);
            var s1 = result.Single(r => r.SessionId == "s1");
            s1.ApplicationCount.Should().Be(2);
            s1.ClientIds.Should().BeEquivalentTo(new[] { "app1", "app2" });
            s1.CreatedAt.Should().Be(new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc));
            s1.PrimaryDeviceName.Should().Be("Pixel");
        }

        [Fact]
        public async Task GetUserSessionsAsync_WithClientIdFilter_Works()
        {
            var refresh = RegisterRefresh();
            MongoMock.SetupAggregate(refresh, new List<RefreshTokenModel> { Token(session: "s1", client: "app1") });
            RegisterIdp();
            var result = await Sut().GetUserSessionsAsync("u1", "app1", activeOnly: false, CancellationToken.None);
            result.Should().ContainSingle();
        }

        [Fact]
        public async Task GetUserSessionAsync_EmptyArgs_ReturnsNull()
        {
            (await Sut().GetUserSessionAsync("", "s1", CancellationToken.None)).Should().BeNull();
            (await Sut().GetUserSessionAsync("u1", "", CancellationToken.None)).Should().BeNull();
        }

        [Fact]
        public async Task GetUserSessionAsync_NoRows_ReturnsNull()
        {
            RegisterRefresh();
            RegisterIdp();
            (await Sut().GetUserSessionAsync("u1", "s1", CancellationToken.None)).Should().BeNull();
        }

        [Fact]
        public async Task GetUserSessionAsync_ActiveRows_ReturnsActiveSession()
        {
            RegisterRefresh(new[] { Token(session: "s1", client: "app1"), Token(session: "s1", client: "app2") });
            RegisterIdp(new[] { new IdpSessionModel { SessionId = "s1", CreatedAt = DateTime.UtcNow.AddDays(-1), LastActivityAt = DateTime.UtcNow } });

            var result = await Sut().GetUserSessionAsync("u1", "s1", CancellationToken.None);

            result.Should().NotBeNull();
            result!.Status.Should().Be(SessionStatus.Active);
            result.ApplicationCount.Should().Be(2);
        }

        [Fact]
        public async Task GetUserSessionAsync_AllRevoked_ReturnsExpiredStatus()
        {
            RegisterRefresh(new[] { Token(session: "s1", revoked: true) });
            RegisterIdp();

            var result = await Sut().GetUserSessionAsync("u1", "s1", CancellationToken.None);

            result!.Status.Should().Be(SessionStatus.Expired);
        }

        [Fact]
        public async Task GetRotationHistoryAsync_EmptySession_ReturnsEmpty()
        {
            (await Sut().GetRotationHistoryAsync("", CancellationToken.None)).Should().BeEmpty();
        }

        [Fact]
        public async Task GetRotationHistoryAsync_MarksLastActiveAsCurrent()
        {
            RegisterRefresh(new[]
            {
                Token(session: "s1", client: "app1"),
                Token(session: "s1", client: "app2")
            });

            var result = await Sut().GetRotationHistoryAsync("s1", CancellationToken.None);

            result.Should().HaveCount(2);
            result[^1].IsCurrent.Should().BeTrue();
            result[0].IsCurrent.Should().BeFalse();
        }

        [Fact]
        public async Task EnsureIndexesAsync_CreatesIndexesOnBothCollections()
        {
            var refresh = RegisterRefresh();
            MongoMock.SetupIndexes(refresh);
            var impersonation = RegisterImpersonation();
            MongoMock.SetupIndexes(impersonation);

            await Sut().EnsureIndexesAsync(CancellationToken.None);

            refresh.Verify(c => c.Indexes, Times.AtLeastOnce);
            impersonation.Verify(c => c.Indexes, Times.AtLeastOnce);
        }
    }
}
