using Blocks.Genesis;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Request to verify a previously issued captcha verification code.
/// </summary>
public class VerifyCaptchaRequest
{
    /// <summary>Opaque verification code returned by the submit step.</summary>
    public string VerificationCode { get; set; } = string.Empty;

    /// <summary>Provider name to use for verification (e.g. <c>recaptcha</c>, <c>hcaptcha</c>).</summary>
    public string ConfigurationName { get; set; } = string.Empty;
}

/// <summary>
/// Response payload returned from <see cref="ICaptchaService.VerifyCaptchaAsync"/>.
/// </summary>
public class VerifyCaptchaRequestResponse : BaseMutationResponse
{
    /// <summary>
    /// Creates a default unverified response.
    /// </summary>
    public VerifyCaptchaRequestResponse()
    {
        Verified = false;
        HostName = string.Empty;
    }

    /// <summary>Whether the verification succeeded.</summary>
    public bool Verified { get; set; }

    /// <summary>Host name of the verified captcha, when applicable.</summary>
    public string HostName { get; set; }
}
