namespace Blocks.CaptchaDriver;

/// <summary>
/// Top-level service that issues and verifies captcha verification codes.
/// </summary>
public interface ICaptchaService
{
    /// <summary>
    /// Issues a one-time verification code for a captcha submission.
    /// </summary>
    /// <param name="command">Submission request containing the captcha identifier, value, and host name.</param>
    Task<SubmitCaptchaRequestResponse> SubmitCaptchaAsync(SubmitCaptchaRequest command);

    /// <summary>
    /// Verifies a previously issued verification code against the configured provider.
    /// </summary>
    /// <param name="query">Verification request containing the code and configuration name.</param>
    Task<VerifyCaptchaRequestResponse> VerifyCaptchaAsync(VerifyCaptchaRequest query);
}
