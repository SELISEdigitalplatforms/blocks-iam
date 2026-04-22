using Blocks.Genesis;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared.RequestModel;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class MfaController : ControllerBase
    {
        private readonly IMfaManagementService _mfaManagementService;
        private readonly TotpService _totpService;
        private readonly ChangeControllerContext _changeControllerContext;

        public MfaController(IMfaManagementService mfaManagementService,
                            TotpService totpService,
                            ChangeControllerContext changeControllerContext)
        {
            _changeControllerContext = changeControllerContext;
            _mfaManagementService = mfaManagementService;
            _totpService = totpService;
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<OtpGenerationResponse> GenerateOTP([FromBody] OtpGenerationRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _mfaManagementService.GenerateOTPAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<OtpVerificationResponse> VerifyOTP([FromBody] VerifyOtpRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _mfaManagementService.VerifyOTPAsync(request);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<BaseResponse> DisableUserMfa([FromBody] DisableUserMfaRequest request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _mfaManagementService.DisableUserMfa(request);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<SetUpUserTotpResponse> SetUpTotp([FromQuery] SetUpUserTotpRequest request)
        {
            _changeControllerContext.ChangeContext(request);

            if (string.IsNullOrWhiteSpace(request.UserId))
                return new SetUpUserTotpResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "empty_user_id", "User id should not be empty" } } };

            return await _totpService.GenerateTotpImageByUserAsync(request.UserId);
        }

        [ProtectedEndPoint]
        [HttpPost]
        public async Task<OtpGenerationResponse> ResendOtp([FromBody] ResendOtpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MfaId)) return new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "empty_mfa_id", "Mfa id should not be empty" } } };

            return await _mfaManagementService.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain);
        }
    }
}
