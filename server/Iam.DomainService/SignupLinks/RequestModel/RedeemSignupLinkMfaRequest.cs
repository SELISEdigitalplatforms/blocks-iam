namespace Iam.DomainService.SignupLinks;

public class RedeemSignupLinkMfaRequest
{
    public string MfaId { get; set; } = string.Empty;
    public string MfaCode { get; set; } = string.Empty;
}
