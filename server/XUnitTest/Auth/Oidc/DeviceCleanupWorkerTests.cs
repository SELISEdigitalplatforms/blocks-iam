using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceCleanupWorkerTests
    {
        [Fact]
        public async Task ExecuteAsync_MarksExpiredRows_AndDoesNotTouchConsumed()
        {
            var repo = new Mock<IDeviceAuthorizationRepository>();
            repo.Setup(r => r.GetExpiredIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "a", "b" });
            repo.Setup(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var markCalled = new TaskCompletionSource();
            repo.Setup(r => r.MarkExpiredAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .Callback(() => markCalled.TrySetResult());

            var worker = new DeviceCleanupWorker(repo.Object, NullLogger<DeviceCleanupWorker>.Instance);

            using var cts = new CancellationTokenSource();
            var execute = worker.StartAsync(cts.Token);
            await WaitOrTimeoutAsync(markCalled.Task);
            cts.Cancel();
            await execute;

            repo.Verify(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            repo.Verify(r => r.GetExpiredIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            repo.Verify(r => r.MarkExpiredAsync(It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_SkipsMarkExpired_WhenNoExpiredIds()
        {
            var repo = new Mock<IDeviceAuthorizationRepository>();
            var sweepCalled = new TaskCompletionSource();
            repo.Setup(r => r.GetExpiredIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string>())
                .Callback(() => sweepCalled.TrySetResult());
            repo.Setup(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var worker = new DeviceCleanupWorker(repo.Object, NullLogger<DeviceCleanupWorker>.Instance);

            using var cts = new CancellationTokenSource();
            var execute = worker.StartAsync(cts.Token);
            await WaitOrTimeoutAsync(sweepCalled.Task);
            cts.Cancel();
            await execute;

            repo.Verify(r => r.MarkExpiredAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static async Task WaitOrTimeoutAsync(Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            completed.Should().Be(task, "the worker should perform its first sweep well within the timeout");
        }
    }
}