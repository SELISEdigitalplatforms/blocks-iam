using Microsoft.Extensions.Options;
using Worker.Configuration;

namespace Worker
{
    public class PeriodicPingBackgroundService : BackgroundService
    {
        private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<PeriodicPingConfiguration> _optionsMonitor;
        private readonly ILogger<PeriodicPingBackgroundService> _logger;

        private PeriodicTimer? _timer;

        public PeriodicPingBackgroundService(
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<PeriodicPingConfiguration> optionsMonitor,
            ILogger<PeriodicPingBackgroundService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var config = _optionsMonitor.CurrentValue;

            if (!config.Enabled)
            {
                _logger.LogInformation("Periodic ping is disabled.");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.PingUrl))
            {
                _logger.LogWarning("Periodic ping is enabled but PingUrl is empty. Service will not start.");
                return;
            }

            if (config.PingIntervalSeconds <= 0)
            {
                _logger.LogWarning(
                    "Periodic ping is enabled but PingIntervalSeconds is {Value}. Service will not start.",
                    config.PingIntervalSeconds);
                return;
            }

            _timer = new PeriodicTimer(TimeSpan.FromSeconds(config.PingIntervalSeconds));

            try
            {
                await PingAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await _timer.WaitForNextTickAsync(stoppingToken);
                    await PingAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic ping failed");
            }
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task PingAsync(CancellationToken ct)
        {
            var config = _optionsMonitor.CurrentValue;
            var client = _httpClientFactory.CreateClient();

            try
            {
                _logger.LogInformation("Pinging {Url}", config.PingUrl);

                using var response = await client.GetAsync(config.PingUrl, ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Ping success ({StatusCode})", response.StatusCode);
                    return;
                }

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    _logger.LogWarning(
                        "Ping failed with client error {StatusCode}. Check PingUrl.",
                        response.StatusCode);
                    return;
                }

                _logger.LogError(
                    "Ping failed with server error {StatusCode}. Will retry later.",
                    response.StatusCode);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Ping timed out");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ping request failed");
            }
        }
    }
}
