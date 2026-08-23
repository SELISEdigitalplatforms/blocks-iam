namespace Blocks.CaptchaDriver;

/// <summary>
/// Verifies a reCAPTCHA token using a secret that has already been resolved — either the inline
/// value from a legacy configuration, or the plaintext read from the vault for a blocks-os one.
/// </summary>
/// <remarks>
/// Takes the resolved secret rather than the configuration on purpose: resolving a
/// <c>SecretId</c> is asynchronous and may fail, and neither belongs in a constructor.
/// </remarks>
public sealed class DbReCaptchaConfig : IRecaptchaConfig
{
    private readonly string _verificationUriTemplate;
    private readonly string? _token;

    public DbReCaptchaConfig(string captchaSecret, string? token)
    {
        _token = token;
        _verificationUriTemplate =
            $"https://www.google.com/recaptcha/api/siteverify?secret={Uri.EscapeDataString(captchaSecret ?? string.Empty)}&response={{0}}";
    }

    /// <inheritdoc />
    public string ResolveRecaptchaUri()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, _verificationUriTemplate, _token);
    }
}
