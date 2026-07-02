using Blocks.Genesis;
using Microsoft.Extensions.Options;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Creates short-lived captcha verification codes backed by a cache and forwards
/// verification requests to the correct provider-specific service.
/// </summary>
public sealed class CaptchaProcessor : ICaptchaProcessor
{
    private const string VerificationCodeCacheKeyPrefix = "captcha:vc:";

    private readonly ICacheClient _cache;
    private readonly ICaptchaVerificationServiceProvider _captchaVerificationServiceProvider;
    private readonly CaptchaOptions _options;

    public CaptchaProcessor(
        ICacheClient cache,
        ICaptchaVerificationServiceProvider captchaVerificationServiceProvider,
        IOptions<CaptchaOptions> options)
    {
        _cache = cache;
        _captchaVerificationServiceProvider = captchaVerificationServiceProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string> SubmitAndCreateVerificationCodeAsync(string? captchaId, string? hostName)
    {
        if (captchaId is null)
        {
            throw new ArgumentNullException(nameof(captchaId));
        }

        if (hostName is null)
        {
            throw new ArgumentNullException(nameof(hostName));
        }

        var verificationCode = Guid.NewGuid().ToString("n");
        var cacheKey = VerificationCodeCacheKeyPrefix + verificationCode;
        var ttl = Math.Max(1L, _options.VerificationCodeTtlSeconds);

        await _cache.AddStringValueAsync(cacheKey, hostName, ttl);
        await _cache.RemoveKeyAsync(captchaId);

        return verificationCode;
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyCaptchaAsync(string? configProvider, string? verificationCode)
    {
        if (verificationCode is null)
        {
            throw new ArgumentNullException(nameof(verificationCode));
        }

        var handler = _captchaVerificationServiceProvider.GetCaptchaVerificationService(configProvider);
        return await handler.VerifyAsync(VerificationCodeCacheKeyPrefix + verificationCode);
    }
}
