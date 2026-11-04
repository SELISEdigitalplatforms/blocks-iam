namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkContextService
{
    Task<SignupLinkContextResponse> GetContextAsync(string? code, string? tenantIdHint);
}
