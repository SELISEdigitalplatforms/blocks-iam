using Blocks.Genesis;

namespace Iam.DomainService.SignupLinks;

public interface ISignupLinkConfigurationService
{
    Task<BaseMutationResponse> CreateAsync(CreateSignupLinkConfigurationRequest request);
    Task<BaseMutationResponse> UpdateAsync(string id, UpdateSignupLinkConfigurationRequest request);
    Task<BaseMutationResponse> ArchiveAsync(string id);
    Task<(SignupLinkConfigurationResponse? Response, bool NotFound)> GetByIdAsync(string id);
    Task<(SignupLinkConfigurationListResponse? Response, Dictionary<string, string>? Errors)> QueryAsync(
        QuerySignupLinkConfigurationsRequest request);
}
