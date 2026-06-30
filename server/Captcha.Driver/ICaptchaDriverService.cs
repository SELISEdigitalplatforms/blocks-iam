namespace Blocks.CaptchaDriver
{
    /// <summary>
    /// Service for handling CAPTCHA operations including submission and verification.
    /// </summary>
    public interface ICaptchaDriverService
    {
        /// <summary>
        /// Submits a CAPTCHA and generates a verification code.
        /// </summary>
        /// <param name="command">The request containing the CAPTCHA Id.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response with the VerificationCode and IsValid status.</returns>
        Task<SubmitCaptchaRequestResponse> Submit(SubmitCaptchaRequest command);

        /// <summary>
        /// Verifies a CAPTCHA using the provided verification code and configuration name.
        /// </summary>
        /// <param name="query">The request containing the VerificationCode and ConfigurationName.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response with the VerificationResult and IsValid status.</returns>
        Task<VerifyCaptchaRequestResponse> Verify(VerifyCaptchaRequest query);
    }
}
