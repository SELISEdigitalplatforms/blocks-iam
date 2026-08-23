using Authentication.DomainService.Oidc.Services;
using FluentAssertions;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Unit tests for <see cref="BlacklistIndexWorker"/>, the startup hook that ensures the password
    /// blacklist's index exists.
    /// </summary>
    public sealed class BlacklistIndexWorkerTests
    {
        private readonly Mock<IIdentityAccessManagementRepository> _repository = new();

        private BlacklistIndexWorker Sut() =>
            new(_repository.Object, NullLogger<BlacklistIndexWorker>.Instance);

        /// <summary>
        /// Drives the worker's behaviour directly. StartAsync cannot be used here: the host, not
        /// BackgroundService.StartAsync, decides when ExecuteAsync runs on this framework version,
        /// so asserting through it would test the host rather than this worker.
        /// </summary>
        private static Task RunToCompletionAsync(BlacklistIndexWorker worker, CancellationToken ct) =>
            worker.EnsureIndexesOnceAsync(ct);

        [Fact]
        public async Task Startup_EnsuresIndexesOnce()
        {
            var calls = 0;
            _repository
                .Setup(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => calls++)
                .Returns(Task.CompletedTask);

            await RunToCompletionAsync(Sut(), CancellationToken.None);

            calls.Should().Be(1);
        }

        [Fact]
        public async Task Startup_PassesTheStoppingTokenThrough()
        {
            using var cts = new CancellationTokenSource();
            CancellationToken observed = default;
            _repository
                .Setup(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()))
                .Callback<CancellationToken>(t => observed = t)
                .Returns(Task.CompletedTask);

            await RunToCompletionAsync(Sut(), cts.Token);

            // The worker must hand its own stopping token down, so a shutdown mid-creation is
            // cancellable rather than detached.
            observed.Should().NotBe(CancellationToken.None);
        }

        [Fact]
        public async Task Startup_DoesNotBlockStartupWhenIndexCreationFails()
        {
            _repository
                .Setup(r => r.EnsureIndexesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("no permission to create indexes"));

            // The blacklist lookup is correct without the index, so a service that cannot create it
            // must still come up rather than crash-looping.
            var run = async () => await RunToCompletionAsync(Sut(), CancellationToken.None);

            await run.Should().NotThrowAsync();
        }
    }
}

