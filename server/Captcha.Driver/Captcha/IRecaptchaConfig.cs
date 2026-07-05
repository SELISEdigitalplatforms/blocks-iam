namespace Blocks.CaptchaDriver;

/// <summary>
/// Builds a reCAPTCHA siteverify request URI from either a database-stored or locally configured secret.
/// </summary>
public interface IRecaptchaConfig
{
    /// <summary>
    /// Returns the fully composed siteverify request URI for the current token.
    /// </summary>
    string ResolveRecaptchaUri();
}
