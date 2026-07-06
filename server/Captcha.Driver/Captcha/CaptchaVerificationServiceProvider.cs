using Microsoft.Extensions.Logging;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves an <see cref="ICaptchaVerificationService"/> by provider name.
/// </summary>
public sealed class CaptchaVerificationServiceProvider : ICaptchaVerificationServiceProvider
{
    private readonly Dictionary<string, ICaptchaVerificationService> _services;
    private readonly ILogger<CaptchaVerificationServiceProvider> _logger;

    /// <summary>
    /// Provider name used when no configuration is supplied.
    /// </summary>
    public const string DefaultProvider = "bcaptcha";

    public CaptchaVerificationServiceProvider(
        IEnumerable<ICaptchaVerificationService> services,
        ILogger<CaptchaVerificationServiceProvider> logger)
    {
        _logger = logger;
        _services = services.ToDictionary(
            s => s.Provider,
            s => s,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ICaptchaVerificationService GetCaptchaVerificationService(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = DefaultProvider;
        }

        if (_services.TryGetValue(provider, out var service))
        {
            return service;
        }

        _logger.LogError("Unknown captcha provider requested: {Provider}", provider);
        throw new InvalidOperationException(
            $"No captcha verification service is registered for provider '{provider}'.");
    }
}
