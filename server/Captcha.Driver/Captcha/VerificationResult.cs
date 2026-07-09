namespace Blocks.CaptchaDriver;

/// <summary>
/// Outcome of a captcha verification request.
/// </summary>
public class VerificationResult
{
    /// <summary>
    /// Creates a new unverified result with an empty host name.
    /// </summary>
    public VerificationResult()
    {
        Verified = false;
        HostName = string.Empty;
    }

    /// <summary>Whether the captcha was verified.</summary>
    public bool Verified { get; set; }

    /// <summary>Originating host name (when applicable).</summary>
    public string HostName { get; set; }

    /// <summary>Field-level errors when verification fails.</summary>
    public IDictionary<string, string>? Errors { get; set; }

    /// <summary>
    /// Projects this result onto a <see cref="VerifyCaptchaRequestResponse"/>.
    /// </summary>
    public VerifyCaptchaRequestResponse ToVerifyCaptchaQueryResponse()
    {
        return new VerifyCaptchaRequestResponse
        {
            Errors = Errors,
            HostName = HostName,
            Verified = Verified,
            IsSuccess = true
        };
    }
}
