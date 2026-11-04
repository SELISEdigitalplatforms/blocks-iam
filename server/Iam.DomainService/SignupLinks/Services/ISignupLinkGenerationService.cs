namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkGenerationService
{
    Task<GenerateSignupLinkResult> GenerateAsync(GenerateSignupLinkRequest request);
    Task<(SignupLinkListResponse? Response, Dictionary<string, string>? Errors)> QueryAsync(QuerySignupLinksRequest request);
    Task<RevokeSignupLinkResponse> RevokeAsync(string linkId);
    Task<RevokeSignupLinksByConfigurationResponse> RevokeByConfigurationAsync(RevokeSignupLinksByConfigurationRequest request);
}
