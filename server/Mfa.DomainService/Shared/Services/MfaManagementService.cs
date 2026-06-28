using Blocks.Genesis;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Iam.DomainService.Entities;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Services
{
    public class MfaManagementService : IMfaManagementService
    {
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly IMfaManagementRepository _mfaRepository;
        private readonly IMfaConfigurationService _configurationService;
        private readonly IMfaAuditService? _auditService;
        private readonly ICacheClient _cacheClient;

        public MfaManagementService(IOtpServiceFactory otpServiceFactory,
                                    IMfaManagementRepository mdmRepository,
                                    IMfaConfigurationService configurationService,
                                    ICacheClient cacheClient,
                                    IMfaAuditService? auditService = null)
        {
            _otpServiceFactory = otpServiceFactory;
            _mfaRepository = mdmRepository;
            _configurationService = configurationService;
            _cacheClient = cacheClient;
            _auditService = auditService;
        }

        public async Task<OtpGenerationResponse> GenerateOTPAsync(OtpGenerationRequest request)
        {
            var configuration = await _configurationService.GetAsync();
            var isConfigurationExist = configuration?.EnableMfa ?? false;

            if (!isConfigurationExist)
            {
                return new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "mfa_not_enable", "Please enable mfa for your application first" } } };
            }

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "empty_user_id", "Mfa is not enable for this user" } } };
            }

            var userInfo = await _mfaRepository.GetItemAsync<UserInfo>(u => u.ItemId == request.UserId, "Users");

            var otpService = _otpServiceFactory.GetOTPService(request?.MfaType ?? userInfo.UserMfaType);
            return await otpService.GenerateAsync(userInfo, request?.SendPhoneNumberAsEmailDomain ?? "");
        }

        public async Task<OtpVerificationResponse> VerifyOTPAsync(VerifyOtpRequest request)
        {
            var otpService = _otpServiceFactory.GetOTPService(request.AuthType);
            var verificationResponse = await otpService.VerifyAsync(request);

            if (verificationResponse.IsValid)
            {
                if (!request.IsFromTokenCall)
                {
                    var updates = new Dictionary<string, object>
                              {
                                 { nameof(UserMfaInfo.MfaEnabled), true },
                                 { nameof(UserMfaInfo.UserMfaType), request.AuthType },
                                 { nameof(UserMfaInfo.IsMfaVerified), true }
                              };

                    await _mfaRepository.UpdatePartialAsync<UserMfaInfo>(verificationResponse.UserId, updates, "Users");
                }

                await WriteAuditAsync("mfa_verification_success", verificationResponse.UserId, request.AuthType, "success");
            }
            else
            {
                await WriteAuditAsync("mfa_verification_failure", request.MfaId, request.AuthType, "failure", verificationResponse.Errors);
            }

            return verificationResponse;
        }

        public async Task<BaseResponse> DisableUserMfa(DisableUserMfaRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.UserId))
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "empty_user_id", "User id should not be empty" } } };
            }

            var currentUserId = BlocksContext.GetContext()?.UserId;
            var isSelfDisable = string.Equals(request.UserId, currentUserId, StringComparison.OrdinalIgnoreCase);
            var isAdmin = !string.IsNullOrWhiteSpace(request.AdminActorUserId);

            if (!isSelfDisable && !isAdmin)
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "invalid_user_id", "Yor are not allowed to disable mfa" } } };
            }

            var previousInfo = await _mfaRepository.GetItemAsync<UserInfo>(u => u.ItemId == request.UserId, "Users");

            var updates = new Dictionary<string, object>
                          {
                             { nameof(UserMfaInfo.MfaEnabled), false },
                             { nameof(UserMfaInfo.UserMfaType), UserMfaType.None },
                             { nameof(UserMfaInfo.IsMfaVerified), false }
                          };

            await _mfaRepository.UpdatePartialAsync<UserMfaInfo>(request.UserId, updates, "Users");

            if (isAdmin)
            {
                await WriteAuditAsync("mfa_reset", request.UserId, previousInfo?.UserMfaType, "success",
                    new Dictionary<string, string> { { "actor", request.AdminActorUserId ?? "admin" }, { "reason", request.Reason ?? "" } });
            }
            else
            {
                await WriteAuditAsync("mfa_disabled", request.UserId, previousInfo?.UserMfaType, "success");
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<OtpGenerationResponse> ResendOtpAsync(string mfaId, string sendPhoneNumberAsEmailDomain)
        {
            var isKeyExist = await _cacheClient.KeyExistsAsync(mfaId);

            if (!isKeyExist)
            {
                return new OtpGenerationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } } };
            }

            var keyValue = await _cacheClient.GetStringValueAsync(mfaId);
            var mfaContext = MfaAuthenticationContext.Deserialize(keyValue);

            return await GenerateOTPAsync(new OtpGenerationRequest { UserId = mfaContext.UserId, MfaType = UserMfaType.Email, SendPhoneNumberAsEmailDomain = sendPhoneNumberAsEmailDomain });
        }

        private async Task WriteAuditAsync(string eventType, string? userId, UserMfaType? mfaType, string status, IDictionary<string, string>? details = null)
        {
            if (_auditService == null || string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            await _auditService.WriteAsync(new MfaAuditEvent
            {
                EventType = eventType,
                UserId = userId,
                MfaType = mfaType,
                Status = status,
                Severity = status == "failure" ? "WARN" : "INFO",
                Details = details == null ? eventType : string.Join(",", details.Select(kv => $"{kv.Key}={kv.Value}"))
            });
        }
    }
}
