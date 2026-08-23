using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves a reCAPTCHA configuration from the secret store, falling back to local configuration
/// only when the tenant has no captcha configuration at all.
/// </summary>
public sealed class RecaptchaConfigFactory : IRecaptchaConfigFactory
{
    private readonly ILogger<RecaptchaConfigFactory> _logger;
    private readonly ICaptchaConfigurationService _captchaConfigurationService;
    private readonly ICaptchaSecretResolver _secretResolver;
    private readonly CaptchaOptions _options;

    public RecaptchaConfigFactory(
        ILogger<RecaptchaConfigFactory> logger,
        ICaptchaConfigurationService captchaConfigurationService,
        ICaptchaSecretResolver secretResolver,
        IOptions<CaptchaOptions> options)
    {
        _logger = logger;
        _captchaConfigurationService = captchaConfigurationService;
        _secretResolver = secretResolver;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IRecaptchaConfig?> GetRecaptchaConfig(string? reCaptchaVerificationUriFormat, string? token)
    {
        if (reCaptchaVerificationUriFormat is null || token is null)
        {
            throw new ArgumentNullException(nameof(reCaptchaVerificationUriFormat));
        }

        CaptchaConfiguration? config;

        try
        {
            config = await _captchaConfigurationService.GetCaptchaConfigurationAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load reCAPTCHA config from store; falling back to local config");
            return new LocalReCaptchaConfig(reCaptchaVerificationUriFormat, token);
        }

        if (config is null)
        {
            _logger.LogDebug("No reCAPTCHA config in store; using local config");
            return new LocalReCaptchaConfig(reCaptchaVerificationUriFormat, token);
        }

        var secret = await config.ResolveSecretAsync(_secretResolver).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(secret))
        {
            // The tenant IS configured, so falling back to the local secret would verify against
            // the wrong key pair. Fail closed instead.
            _logger.LogError(
                "The captcha secret for the active configuration could not be resolved; verification will fail closed.");
            return null;
        }

        return new DbReCaptchaConfig(secret, token);
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetConfigFromDb()
    {
        return await _captchaConfigurationService.GetCaptchaConfigurationAsync().ConfigureAwait(false);
    }
}
