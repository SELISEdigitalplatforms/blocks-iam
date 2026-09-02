using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Migrations
{
    /// <summary>Runs the idempotent migration once when the IAM Worker starts.</summary>
    public sealed class OidcUiTemplateMigrationHostedService : IHostedService
    {
        private readonly OidcUiTemplateMigrationService _migration;
        private readonly ILogger<OidcUiTemplateMigrationHostedService> _logger;

        public OidcUiTemplateMigrationHostedService(
            OidcUiTemplateMigrationService migration,
            ILogger<OidcUiTemplateMigrationHostedService> logger)
        {
            _migration = migration;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _migration.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("OIDC UI template migration was cancelled during Worker startup.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "OIDC UI template migration could not start.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
