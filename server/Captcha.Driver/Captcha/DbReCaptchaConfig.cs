namespace Blocks.CaptchaDriver;

/// <summary>
/// Verifies a reCAPTCHA token using a secret configured in the secret store (database).
/// </summary>
public sealed class DbReCaptchaConfig : IRecaptchaConfig
{
    private readonly string _verificationUriTemplate;
    private readonly string? _token;

    public DbReCaptchaConfig(CaptchaConfiguration config, string? token)
    {
        ArgumentNullException.ThrowIfNull(config);

        _token = token;
        _verificationUriTemplate =
            $"https://www.google.com/recaptcha/api/siteverify?secret={Uri.EscapeDataString(config.CaptchaSecret ?? string.Empty)}&response={{0}}";
    }

    /// <inheritdoc />
    public string ResolveRecaptchaUri()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, _verificationUriTemplate, _token);
    }
}
