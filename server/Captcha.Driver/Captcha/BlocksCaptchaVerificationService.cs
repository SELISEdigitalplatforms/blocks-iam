using Blocks.Genesis;

namespace Blocks.CaptchaDriver;

/// <summary>
/// In-memory captcha verification backed by a cache. Used as the default
/// <c>bcaptcha</c> provider for trusted callers. Verifies only codes
/// <see cref="CaptchaProcessor"/> issued (cache key prefix <c>captcha:vc:</c>); each is single use.
/// </summary>
public sealed class BlocksCaptchaVerificationService : ICaptchaVerificationService
{
    /// <inheritdoc />
    public string Provider => "bcaptcha";

    private readonly ICacheClient _cache;

    public BlocksCaptchaVerificationService(ICacheClient cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(string? verificationCode)
    {
        if (verificationCode is null)
        {
            throw new ArgumentNullException(nameof(verificationCode));
        }

        // Only keys this provider issued (CaptchaProcessor: prefix + code) count as a solved
        // captcha. Any other Redis key, e.g. an oidc_context entry, must never verify.
        // The issued raw code and the full prefixed key are both accepted.
        var code = verificationCode.Trim();
        var cacheKey = code.StartsWith(CaptchaProcessor.VerificationCodeCacheKeyPrefix, StringComparison.Ordinal)
            ? code
            : CaptchaProcessor.VerificationCodeCacheKeyPrefix + code;
        var hasCode = cacheKey.Length > CaptchaProcessor.VerificationCodeCacheKeyPrefix.Length;

        var savedHostName = hasCode ? await _cache.GetStringValueAsync(cacheKey) : null;
        // Single use: of parallel requests with the same code, only the one that really deleted it passes.
        var verified = !string.IsNullOrWhiteSpace(savedHostName) && await _cache.RemoveKeyAsync(cacheKey);

        if (!verified)
        {
            return new VerificationResult
            {
                Verified = false,
                Errors = new Dictionary<string, string>
                {
                    { "VerificationCode", "Verification code incorrect or expired." }
                }
            };
        }

        return new VerificationResult
        {
            Verified = true,
            HostName = savedHostName
        };
    }
}
