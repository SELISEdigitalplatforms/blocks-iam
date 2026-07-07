namespace Blocks.CaptchaDriver;

/// <summary>
/// Verifies a reCAPTCHA token using the locally configured secret (from configuration only).
/// Used as a fallback when no secret-store entry is available.
/// </summary>
public sealed class LocalReCaptchaConfig : IRecaptchaConfig
{
    private readonly string _verificationUriTemplate;
    private readonly string? _token;

    public LocalReCaptchaConfig(string? verificationUriTemplate, string? token)
    {
        _verificationUriTemplate = verificationUriTemplate ?? throw new ArgumentNullException(nameof(verificationUriTemplate));
        _token = token;
    }

    /// <inheritdoc />
    public string ResolveRecaptchaUri()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, _verificationUriTemplate, _token);
    }
}
