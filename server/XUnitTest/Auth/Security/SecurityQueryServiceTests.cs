using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Security
{
    public class SecurityQueryServiceTests
    {
        [Fact]
        public async Task GetSessionsAsync_PopulatesRotationCountAndLastRotatedAt()
        {
            var securityRepo = new Mock<ISecurityRepository>();
            securityRepo.Setup(r => r.GetSessionsAsync("user-1", It.IsAny<string?>(), It.IsAny<GetSessionsRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SessionDto>
                {
                    new() { SessionId = "session-1", UserId = "user-1" }
                });

            var baseRotations = new List<RefreshTokenRotationDto>
            {
                new() { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(-2), IsRevoked = true },
                new() { TokenId = "rt-2", AbsoluteExpiry = DateTime.UtcNow.AddDays(-1), IsRevoked = true },
                new() { TokenId = "rt-3", AbsoluteExpiry = DateTime.UtcNow, IsRevoked = false }
            };

            securityRepo.Setup(r => r.GetRotationHistoryAsync("session-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(baseRotations);

            var service = CreateService(securityRepo.Object, httpContext: null);

            var response = await service.GetSessionsAsync("user-1", new GetSessionsRequest(), CancellationToken.None);

            response.Data.Should().HaveCount(1);
            var session = response.Data.First();
            session.RotationCount.Should().Be(3);
            session.LastRotatedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task GetSessionTimelineAsync_ReturnsRotationsInOrder()
        {
            var securityRepo = new Mock<ISecurityRepository>();
            securityRepo.Setup(r => r.GetSessionAsync("user-1", It.IsAny<string?>(), "session-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SessionDto { SessionId = "session-1", UserId = "user-1" });

            securityRepo.Setup(r => r.GetRefreshTokenStatusAsync("session-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RefreshTokenStatus { TokenId = "rt-3" });

            securityRepo.Setup(r => r.GetRotationHistoryAsync("session-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RefreshTokenRotationDto>
                {
                    new() { TokenId = "rt-1", AbsoluteExpiry = DateTime.UtcNow.AddDays(-2), IsRevoked = true, RevokeReason = "superseded_by_rotation" },
                    new() { TokenId = "rt-2", AbsoluteExpiry = DateTime.UtcNow.AddDays(-1), IsRevoked = true, RevokeReason = "superseded_by_rotation" },
                    new() { TokenId = "rt-3", AbsoluteExpiry = DateTime.UtcNow, IsRevoked = false }
                });

            var service = CreateService(securityRepo.Object, httpContext: null);

            var timeline = await service.GetSessionTimelineAsync("user-1", "session-1", CancellationToken.None);

            timeline.Should().NotBeNull();
            timeline!.Rotations.Should().HaveCount(3);
            timeline.Rotations[0].TokenId.Should().Be("rt-1");
            timeline.Rotations[0].RevokeReason.Should().Be("superseded_by_rotation");
            timeline.Session!.RotationCount.Should().Be(3);
            timeline.Session.LastRotatedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task GetSessionTimelineAsync_ReturnsNull_WhenSessionMissing()
        {
            var securityRepo = new Mock<ISecurityRepository>();
            securityRepo.Setup(r => r.GetSessionAsync("user-1", It.IsAny<string?>(), "missing", It.IsAny<CancellationToken>()))
                .ReturnsAsync((SessionDto?)null);

            var service = CreateService(securityRepo.Object, httpContext: null);

            var timeline = await service.GetSessionTimelineAsync("user-1", "missing", CancellationToken.None);

            timeline.Should().BeNull();
        }

        private static SecurityQueryService CreateService(ISecurityRepository repository, HttpContext? httpContext)
        {
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(httpContext);

            return new SecurityQueryService(
                NullLogger<SecurityQueryService>.Instance,
                repository,
                accessor.Object);
        }
    }
}
