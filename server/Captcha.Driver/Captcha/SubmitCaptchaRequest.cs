using Blocks.Genesis;
using FluentValidation.Results;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Request to submit a captcha challenge. Returns a one-time verification code.
/// </summary>
public class SubmitCaptchaRequest
{
    /// <summary>Captcha identifier previously issued to the client.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Captcha value entered by the user (e.g. text answer).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Originating host name of the client submitting the captcha.</summary>
    public string HostName { get; set; } = string.Empty;
}

/// <summary>
/// Response payload returned from <see cref="ICaptchaService.SubmitCaptchaAsync"/>.
/// </summary>
public class SubmitCaptchaRequestResponse : BaseMutationResponse
{
    /// <summary>
    /// Creates a response that surfaces validation errors.
    /// </summary>
    public SubmitCaptchaRequestResponse(ValidationResult? result) : base()
    {
        Errors = result?.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                 ?? new Dictionary<string, string>();
    }

    /// <summary>Opaque verification code the client must present to verify.</summary>
    public string VerificationCode { get; set; } = string.Empty;
}
