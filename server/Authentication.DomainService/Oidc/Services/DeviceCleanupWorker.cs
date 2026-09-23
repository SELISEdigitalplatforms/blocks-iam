using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

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
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly IOptions<DeviceFlowOptions> _options;
        private readonly ILogger<DeviceCleanupWorker> _logger;
        private readonly HashSet<(string TenantId, string ConnectionString, string DatabaseName)> _indexedTargets = new();

        public DeviceCleanupWorker(
            IDeviceAuthorizationRepository repository,
            IDbContextProvider dbContextProvider,
            IBlocksSecret blocksSecret,
            IOptions<DeviceFlowOptions> options,
            ILogger<DeviceCleanupWorker> logger)
        {
            _repository = repository;
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
            // Tenant discovery always comes from the single registry on main. Do not use
            // the hosted service's absent/ambient BlocksContext to choose a database.
            var tenants = _dbContextProvider
                .GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName)
                .GetCollection<Tenant>("Tenants");
            var enabled = await tenants.Find(Builders<Tenant>.Filter.Ne(t => t.IsDisabled, true)).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var tenant in enabled)
            {
                if (ct.IsCancellationRequested) break;
                if (tenant.IsDisabled || string.IsNullOrWhiteSpace(tenant.TenantId)
                    || string.IsNullOrWhiteSpace(tenant.DBName)
                    || string.IsNullOrWhiteSpace(tenant.DbConnectionString))
                {
                    continue;
                }

                try
                {
                    var target = (tenant.TenantId, tenant.DbConnectionString, tenant.DBName);
                    if (!_indexedTargets.Contains(target))
                    {
                        try
                        {
                            await _repository.EnsureIndexesAsync(tenant.TenantId, ct);
                            _indexedTargets.Add(target);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Index creation is retried next sweep, but an index problem must
                            // not prevent expiration of requests in a reachable database.
                            _logger.LogWarning(ex, "DeviceCleanupWorker: index creation failed for tenant {TenantId}", tenant.TenantId);
                        }
                    }

                    var ids = await _repository.GetExpiredIdsAsync(
                        tenant.TenantId, now, _options.Value.NormalizedCleanupBatchLimit, ct);
                    if (ids.Count == 0) continue;

                    if (await _repository.MarkExpiredAsync(tenant.TenantId, ids, ct))
                    {
                        _logger.LogInformation(
                            "DeviceCleanupWorker: marked {Count} device authorization request(s) as expired for tenant {TenantId}",
                            ids.Count, tenant.TenantId);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DeviceCleanupWorker: sweep failed for tenant {TenantId}", tenant.TenantId);
                }
            }
        }
    }
}
