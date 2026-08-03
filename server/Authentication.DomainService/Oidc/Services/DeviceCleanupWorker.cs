using Authentication.DomainService.Oidc.Repositories;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Authentication.DomainService.Oidc.Services
{
    /// <summary>
    /// Sweeps expired device authorization requests on a 5-minute cadence.
    /// Transitions Pending/Approved rows whose <c>ExpiresAt</c> is in the past to <c>Expired</c>;
    /// never touches <c>Consumed</c> rows (RFC 8628 §6.5 audit trail).
    /// </summary>
    public sealed class DeviceCleanupWorker : BackgroundService
    {
        private readonly IDeviceAuthorizationRepository _repository;
        private readonly IOptions<DeviceFlowOptions> _options;
        private readonly ILogger<DeviceCleanupWorker> _logger;

        public DeviceCleanupWorker(
            IDeviceAuthorizationRepository repository,
            IOptions<DeviceFlowOptions> options,
            ILogger<DeviceCleanupWorker> logger)
        {
            _repository = repository;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _repository.EnsureIndexesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeviceCleanupWorker: EnsureIndexesAsync failed during startup.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DeviceCleanupWorker: sweep iteration failed");
                }

                try
                {
                    await Task.Delay(_options.Value.CleanupSweepInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task SweepOnceAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var ids = await _repository.GetExpiredIdsAsync(now, _options.Value.NormalizedCleanupBatchLimit, ct);
            if (ids.Count == 0)
            {
                return;
            }

            var expired = await _repository.MarkExpiredAsync(ids, ct);
            if (expired)
            {
                _logger.LogInformation("DeviceCleanupWorker: marked {Count} device authorization request(s) as expired", ids.Count);
            }
        }
    }
}
