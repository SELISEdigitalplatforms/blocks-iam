namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves the provider secret for a configuration, whichever store it came from.
/// </summary>
public static class CaptchaConfigurationSecretExtensions
{
    /// <summary>
    /// Returns the provider secret to verify with, or <c>null</c> when none can be obtained.
    /// </summary>
    /// <remarks>
    /// A configuration carrying <see cref="CaptchaConfiguration.SecretId"/> came from blocks-os and
    /// its secret lives in the vault; one carrying an inline
    /// <see cref="CaptchaConfiguration.CaptchaSecret"/> is legacy. The two are never mixed: when a
    /// <c>SecretId</c> is present and cannot be resolved, the answer is <c>null</c> and verification
    /// fails closed, rather than silently reaching for some other secret.
    /// </remarks>
    public static async Task<string?> ResolveSecretAsync(
        this CaptchaConfiguration configuration,
        ICaptchaSecretResolver resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(resolver);

        if (!string.IsNullOrWhiteSpace(configuration.SecretId))
        {
            return await resolver.ResolveAsync(configuration.SecretId, cancellationToken).ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(configuration.CaptchaSecret) ? null : configuration.CaptchaSecret;
    }
}
