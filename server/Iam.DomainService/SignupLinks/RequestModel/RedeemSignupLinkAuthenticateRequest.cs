using System.Text.Json.Serialization;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// The password step after redeem answered <c>authentication_required</c>.
/// </summary>
public class RedeemSignupLinkAuthenticateRequest
{
    public string? RedemptionId { get; set; }
    public string? Password { get; set; }

    /// <summary>Same JSON name the login request uses.</summary>
    [JsonPropertyName("captcha_code")]
    public string? CaptchaCode { get; set; }
}
