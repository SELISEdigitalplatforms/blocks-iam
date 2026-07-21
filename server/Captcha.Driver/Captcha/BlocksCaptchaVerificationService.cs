using Blocks.Genesis;

namespace Blocks.CaptchaDriver;

/// <summary>
/// In-memory captcha verification backed by a cache. Used as the default
/// <c>bcaptcha</c> provider for trusted callers. The cache key passed in
/// <see cref="VerifyAsync"/> is fully qualified by the caller; this service
/// does not assume any prefix.
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

        var savedHostName = await _cache.GetStringValueAsync(verificationCode);
        var verified = !string.IsNullOrWhiteSpace(savedHostName);

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

        await _cache.RemoveKeyAsync(verificationCode);

        return new VerificationResult
        {
            Verified = true,
            HostName = savedHostName
        };
    }
}
