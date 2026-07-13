using Authentication.DomainService.Oidc.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceInteractionStateStoreTests
    {
        [Fact]
        public async Task SaveAndGet_RoundTripsContext()
        {
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => new RedisValue("{\"requestId\":\"r1\",\"tenantId\":\"t1\",\"clientId\":\"c1\",\"createdAt\":\"2025-01-01T00:00:00Z\"}"));

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(db.Object);

            var store = new DeviceInteractionStateStore(cache.Object, NullLogger<DeviceInteractionStateStore>.Instance);

            await store.SaveAsync("iid", new DeviceInteractionContext
            {
                RequestId = "r1", TenantId = "t1", ClientId = "c1", CreatedAt = DateTime.UtcNow
            }, TimeSpan.FromMinutes(10));

            var ctx = await store.GetAsync("iid");
            ctx.Should().NotBeNull();
            ctx!.RequestId.Should().Be("r1");
            ctx.TenantId.Should().Be("t1");
            ctx.ClientId.Should().Be("c1");
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenCacheMiss()
        {
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => RedisValue.Null);

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(db.Object);

            var store = new DeviceInteractionStateStore(cache.Object, NullLogger<DeviceInteractionStateStore>.Instance);
            var ctx = await store.GetAsync("missing");
            ctx.Should().BeNull();
        }
    }
}