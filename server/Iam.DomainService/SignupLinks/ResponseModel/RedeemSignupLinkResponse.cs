namespace Iam.DomainService.SignupLinks;

public class RedeemSignupLinkResponse
{
    public string AuthorizeUrl { get; set; } = string.Empty;
}

public class RedeemSignupLinkErrorResponse
{
    public string Error { get; set; } = "invalid_link";
}
