namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkSummaryService
{
    Task<(SignupLinkSummaryResponse? Response, Dictionary<string, string>? Errors)> SummarizeAsync(
        SignupLinkSummaryRequest request);
}
