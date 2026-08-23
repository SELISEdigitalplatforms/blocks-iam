namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves the plaintext of a captcha secret held in the secret store, given the
/// <c>SecretId</c> pointer carried on a captcha configuration.
/// </summary>
/// <remarks>
/// Configuration written by blocks-os keeps only a pointer; the value itself lives in Azure
/// Key Vault. This is the single seam through which that value enters the captcha driver.
/// <para>
/// Every failure is reported as <c>null</c> rather than an exception. Callers sit inside
/// FluentValidation rules and anonymous authentication endpoints, where a propagated
/// secret-store exception would surface as a 500 on a public endpoint and leak the shape of
/// the store. "Could not resolve" and "verification fails closed" are the same outcome here.
/// </para>
/// </remarks>
public interface ICaptchaSecretResolver
{
    /// <summary>
    /// Returns the plaintext secret for <paramref name="secretId"/>, or <c>null</c> when it
    /// cannot be resolved for any reason.
    /// </summary>
    /// <param name="secretId">Secret store item id. Null, empty or whitespace yields <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> ResolveAsync(string? secretId, CancellationToken cancellationToken = default);
}
