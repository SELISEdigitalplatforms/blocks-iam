using Mfa.DomainService.Entities;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Services
{
    public interface IOtpService
    {
        Task<OtpGenerationResponse> GenerateAsync(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null);
        Task<OtpVerificationResponse> VerifyAsync(VerifyOtpRequest request);

        // Re-delivers the code for an existing challenge, preserving the same mfa_id. Methods
        // that have no code to deliver (TOTP) reject the request.
        Task<OtpGenerationResponse> ResendAsync(string mfaId, UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null);
    }
}
