using Iam.DomainService.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Services
{
    /// <summary>
    /// Ensures the password blacklist's (Key, Value) index exists on the root database, once at
    /// startup.
    /// </summary>
    /// <remarks>
    /// Registered through <c>RegisterAllServices()</c>, which both the Api and the Worker call, so
    /// this runs in both processes. That is harmless: creating an index that already exists is a
    /// no-op in MongoDB, so two simultaneous starts cannot produce a duplicate.
    ///
    /// Index creation is a performance concern, never a correctness one - the blacklist lookup is
    /// right with or without it - so a failure here must not stop the service from starting.
    /// </remarks>
    public sealed class BlacklistIndexWorker : BackgroundService
    {
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly ILogger<BlacklistIndexWorker> _logger;

        public BlacklistIndexWorker(
            IIdentityAccessManagementRepository repository,
            ILogger<BlacklistIndexWorker> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            EnsureIndexesOnceAsync(stoppingToken);

        /// <summary>
        /// The whole of this worker's behaviour, exposed directly because the host decides when
        /// <see cref="ExecuteAsync"/> runs and a unit test cannot drive that reliably.
        /// </summary>
        public async Task EnsureIndexesOnceAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _repository.EnsureIndexesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BlacklistIndexWorker: EnsureIndexesAsync failed during startup.");
            }
        }
    }
}
