using Blocks.Genesis;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Services
{
    public interface IMfaManagementService
    {
        Task<OtpGenerationResponse> GenerateOTPAsync(OtpGenerationRequest request);
        Task<OtpVerificationResponse> VerifyOTPAsync(VerifyOtpRequest request);
        Task<string?> GetChallengeOwnerAsync(string mfaId);
        Task<OtpGenerationResponse> ResendOtpAsync(string mfaId, string sendPhoneNumberAsEmailDomain);
        Task<BaseResponse> DisableUserMfa(DisableUserMfaRequest request);
    }
}
