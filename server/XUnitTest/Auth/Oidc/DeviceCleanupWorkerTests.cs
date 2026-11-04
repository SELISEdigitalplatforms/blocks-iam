using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceCleanupWorkerTests
    {
        [Fact]
        public async Task SweepWithoutAmbientContext_VisitsRootDevAndOther_AndSkipsDisabledTenants()
        {
            var (database, secrets) = Registry(
                Tenant("root", "main", "BlocksRootDb"),
                Tenant("dev", "dev-connection", "DevDb"),
                Tenant("stg", "other-connection", "StgDb"),
                Tenant("disabled", "main", "DisabledDb", disabled: true));
            var repository = new Mock<IDeviceAuthorizationRepository>(MockBehavior.Strict);
            var marked = new HashSet<string>();
            var allMarked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            repository.Setup(r => r.EnsureIndexesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.GetExpiredIdsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string tenantId, DateTime _, int _, CancellationToken _) =>
                    new List<string> { $"expired-{tenantId}" });
            repository.Setup(r => r.MarkExpiredAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Callback((string tenantId, IEnumerable<string> _, CancellationToken _) =>
                {
                    lock (marked)
                    {
                        marked.Add(tenantId);
                        if (marked.Count == 3) allMarked.TrySetResult();
                    }
                })
                .ReturnsAsync(true);

            using var worker = Worker(repository.Object, database.Object, secrets.Object);
            await worker.StartAsync(CancellationToken.None);
            await WaitOrTimeoutAsync(allMarked.Task);
            await worker.StopAsync(CancellationToken.None);

            foreach (var id in new[] { "root", "dev", "stg" })
            {
                repository.Verify(r => r.EnsureIndexesAsync(id, It.IsAny<CancellationToken>()), Times.Once);
                repository.Verify(r => r.GetExpiredIdsAsync(id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
                repository.Verify(r => r.MarkExpiredAsync(id,
                    It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { $"expired-{id}" })),
                    It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            }
            repository.Verify(r => r.EnsureIndexesAsync("disabled", It.IsAny<CancellationToken>()), Times.Never);
            repository.Verify(r => r.MarkExpiredAsync("disabled", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
            repository.Verify(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()), Times.Never);
            repository.Verify(r => r.GetExpiredIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FailedTenant_DoesNotPreventOtherTenantsFromBeingSwept()
        {
            var (database, secrets) = Registry(
                Tenant("dev", "dev-connection", "DevDb"),
                Tenant("root", "main", "BlocksRootDb"));
            var repository = new Mock<IDeviceAuthorizationRepository>(MockBehavior.Strict);
            var rootSwept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            repository.Setup(r => r.EnsureIndexesAsync("dev", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("dev unavailable"));
            repository.Setup(r => r.GetExpiredIdsAsync("dev", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("dev unavailable"));
            repository.Setup(r => r.EnsureIndexesAsync("root", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            repository.Setup(r => r.GetExpiredIdsAsync("root", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback(() => rootSwept.TrySetResult())
                .ReturnsAsync(new List<string>());

            using var worker = Worker(repository.Object, database.Object, secrets.Object);
            await worker.StartAsync(CancellationToken.None);
            await WaitOrTimeoutAsync(rootSwept.Task);
            await worker.StopAsync(CancellationToken.None);

            repository.Verify(r => r.EnsureIndexesAsync("dev", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            repository.Verify(r => r.GetExpiredIdsAsync("dev", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            repository.Verify(r => r.GetExpiredIdsAsync("root", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task IndexFailure_StillSweepsReachableTenant()
        {
            var (database, secrets) = Registry(Tenant("root", "main", "BlocksRootDb"));
            var repository = new Mock<IDeviceAuthorizationRepository>(MockBehavior.Strict);
            var swept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            repository.Setup(r => r.EnsureIndexesAsync("root", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("index conflict"));
            repository.Setup(r => r.GetExpiredIdsAsync("root", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "expired-root" });
            repository.Setup(r => r.MarkExpiredAsync("root", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Callback(() => swept.TrySetResult())
                .ReturnsAsync(true);

            using var worker = Worker(repository.Object, database.Object, secrets.Object);
            await worker.StartAsync(CancellationToken.None);
            await WaitOrTimeoutAsync(swept.Task);
            await worker.StopAsync(CancellationToken.None);

            repository.Verify(r => r.EnsureIndexesAsync("root", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            repository.Verify(r => r.MarkExpiredAsync("root", It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "expired-root" })), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        private static DeviceCleanupWorker Worker(
            IDeviceAuthorizationRepository repository, IDbContextProvider database, IBlocksSecret secrets) =>
            new(repository, database, secrets,
                Options.Create(new DeviceFlowOptions { CleanupSweepIntervalSeconds = 1 }),
                NullLogger<DeviceCleanupWorker>.Instance);

        private static (Mock<IDbContextProvider>, Mock<IBlocksSecret>) Registry(params Tenant[] tenants)
        {
            var database = new Mock<IDbContextProvider>(MockBehavior.Strict);
            var rootDatabase = MongoMock.Database();
            MongoMock.OnDatabase(rootDatabase, "Tenants", MongoMock.Collection(tenants));
            database.Setup(d => d.GetDatabase("main", "BlocksRootDb", It.IsAny<bool>()))
                .Returns(rootDatabase.Object);

            var secrets = new Mock<IBlocksSecret>();
            secrets.SetupGet(s => s.DatabaseConnectionString).Returns("main");
            secrets.SetupGet(s => s.RootDatabaseName).Returns("BlocksRootDb");
            return (database, secrets);
        }

        private static Tenant Tenant(string tenantId, string connection, string name, bool disabled = false) => new()
        {
            TenantId = tenantId,
            DBName = name,
            DbConnectionString = connection,
            IsDisabled = disabled,
            JwtTokenParameters = new JwtTokenParameters
            {
                PrivateCertificatePassword = string.Empty,
                IssueDate = DateTime.UtcNow
            }
        };

        private static async Task WaitOrTimeoutAsync(Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            completed.Should().Be(task, "the worker should perform its first sweep well within the timeout");
        }
    }
}
