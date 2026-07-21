namespace Blocks.CaptchaDriver;

/// <summary>
/// Public entry point for the Captcha driver. Consumers should depend on this
/// interface rather than on <see cref="ICaptchaService"/> directly so that the
/// driver can evolve its internal layering without breaking callers.
/// </summary>
public interface ICaptchaDriverService
{
    /// <summary>
    /// Submits a captcha and generates a one-time verification code.
    /// </summary>
    /// <param name="command">The submit request containing the captcha identifier, value, and host name.</param>
    /// <returns>Response with the verification code on success or validation errors.</returns>
    Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command);

    /// <summary>
    /// Verifies a previously issued verification code.
    /// </summary>
    /// <param name="query">The verify request containing the verification code and configuration name.</param>
    /// <returns>Response with the verification outcome and host name on success.</returns>
    Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query);
}
