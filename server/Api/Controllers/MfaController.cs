using System.Text.Json.Serialization;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserEntity = Iam.DomainService.Entities.User;

namespace Api.Controllers;

[ApiController]
[Route("mfa")]
// Deprecated: use "mfa". "api/mfa" kept as a temporary compatibility alias.
[Route("api/mfa")]
public class MfaController : ControllerBase
{
    private readonly IMfaManagementService _mfaManagementService;
    private readonly IMfaConfigurationService _mfaConfigurationService;
    private readonly IMfaBackupCodeService _backupCodeService;
    private readonly IMfaAuditService _auditService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly TotpService _totpService;
    private readonly IUserActivityDispatcher _userActivityDispatcher;

    public MfaController(
        IMfaManagementService mfaManagementService,
        IMfaConfigurationService mfaConfigurationService,
        IMfaBackupCodeService backupCodeService,
        IMfaAuditService auditService,
        IAuthenticationRepository authenticationRepository,
        TotpService totpService,
        IUserActivityDispatcher userActivityDispatcher)
    {
        _mfaManagementService = mfaManagementService;
        _mfaConfigurationService = mfaConfigurationService;
        _backupCodeService = backupCodeService;
        _auditService = auditService;
        _authenticationRepository = authenticationRepository;
        _totpService = totpService;
        _userActivityDispatcher = userActivityDispatcher;
    }

    [HttpGet("config")]
    [ProtectedEndPoint("blocks-iam::iam::mfa-configs")]
    public async Task<IActionResult> GetMfaConfig()
    {
        var config = await _mfaConfigurationService.GetAsync() ?? new Configuration { UserMfaType = new List<UserMfaType>() };
        return Ok(new
        {
            enabled = config.EnableMfa,
            allowedMethods = config.UserMfaType,
            requireMfaForAllUsers = config.RequireMfaForAllUsers,
            mfaRequiredRoles = config.MfaRequiredRoles,
            mfaExemptRoles = config.MfaExemptRoles,
            allowUserOptOut = config.AllowUserOptOut,
            allowBackupCodes = config.AllowBackupCodes,
            backupCodesCount = config.BackupCodesCount
        });
    }

    [HttpPost("config")]
    [ProtectedEndPoint("blocks-iam::iam::mutate-mfa-configs")]
    public async Task<IActionResult> UpdateMfaConfig([FromBody] UpdateMfaPolicyRequest request, CancellationToken ct)
    {
        var current = await _mfaConfigurationService.GetAsync() ?? new Configuration { UserMfaType = new List<UserMfaType>() };
        if (request.EnableMfa.HasValue) current.EnableMfa = request.EnableMfa.Value;
        if (request.UserMfaType != null) current.UserMfaType = request.UserMfaType;
        if (request.RequireMfaForAllUsers.HasValue) current.RequireMfaForAllUsers = request.RequireMfaForAllUsers.Value;
        if (request.MfaRequiredRoles != null) current.MfaRequiredRoles = request.MfaRequiredRoles;
        if (request.MfaExemptRoles != null) current.MfaExemptRoles = request.MfaExemptRoles;
        if (request.AllowUserOptOut.HasValue) current.AllowUserOptOut = request.AllowUserOptOut.Value;
        if (request.AllowBackupCodes.HasValue) current.AllowBackupCodes = request.AllowBackupCodes.Value;
        if (request.BackupCodesCount.HasValue) current.BackupCodesCount = request.BackupCodesCount.Value;
        if (request.MfaTemplate != null) current.MfaTemplate = request.MfaTemplate;

        await _mfaConfigurationService.SaveAsync(current);

        await AuditPolicyAsync("mfa_policy_updated", request);
        return Ok(current);
    }

    // Self-service: a user enrolls their own TOTP. Own-identity is enforced by operating
    // solely on GetCurrentUserId(); no admin config scope is required.
    [HttpPost("totp/setup")]
    [Authorize]
    public async Task<IActionResult> SetupTotp()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var response = await _totpService.GenerateTotpImageByUserAsync(userId);
        if (response?.Errors != null && response.Errors.Count > 0)
        {
            return BadRequest(new { errors = response.Errors });
        }

        return Ok(new
        {
            qrImageUrl = response?.QrImageUrl,
            secret = response?.QrCode
        });
    }

    // Self-service: a user verifies their own TOTP enrollment. Own-identity enforced via GetCurrentUserId().
    [HttpPost("totp/verify-setup")]
    [Authorize]
    public async Task<IActionResult> VerifyTotpSetup([FromBody] VerifyTotpSetupRequest request)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(request?.Code))
        {
            return BadRequest(new { error = "invalid_request", error_description = "userId and code are required" });
        }

        var verification = await _totpService.VerifyForUserAsync(userId, request.Code);
        if (!verification.IsValid)
        {
            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = userId,
                Category = UserActivityCategory.Auth,
                Event = LoginAuditEvents.MfaEnrollmentFailed,
                Source = "auth-mfa-enrollment",
                Outcome = "failure",
                ReasonCode = "invalid_totp_code"
            });
            return BadRequest(new { error = "invalid_totp_code", error_description = "TOTP code is invalid" });
        }

        await _authenticationRepository.UpdatePartialAsync<UserEntity>(userId, new Dictionary<string, object>
        {
            { nameof(UserEntity.MfaEnabled), true },
            { nameof(UserEntity.UserMfaType), UserMfaType.TOTP },
            { nameof(UserEntity.IsMfaVerified), true },
            { nameof(UserEntity.LastUpdatedDate), DateTime.UtcNow },
            { nameof(UserEntity.LastUpdatedBy), userId }
        });

        await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
        {
            UserId = userId,
            Category = UserActivityCategory.Auth,
            Event = LoginAuditEvents.MfaEnrollmentCompleted,
            Source = "auth-mfa-enrollment",
            Outcome = "success",
            Metadata = new Dictionary<string, string> { { "method", UserMfaType.TOTP.ToString() } }
        });

        return Ok(new { enabled = true, method = "TOTP" });
    }

    [HttpPost("generate")]
    [Authorize]
    public async Task<IActionResult> GenerateOtp([FromBody] GenerateOtpRequest? request)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await _mfaManagementService.GenerateOTPAsync(new OtpGenerationRequest
        {
            UserId = userId,
            MfaType = request?.MfaType,
            SendPhoneNumberAsEmailDomain = request?.SendPhoneNumberAsEmailDomain
        });

        if (result.Errors != null && result.Errors.Count > 0)
        {
            return BadRequest(new { errors = result.Errors });
        }

        return Ok(new { mfaId = result.MfaId });
    }

    [HttpPost("resend")]
    [Authorize]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpApiRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MfaId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "mfaId is required" });
        }

        var result = await _mfaManagementService.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain ?? string.Empty);

        if (result.Errors != null && result.Errors.Count > 0)
        {
            return BadRequest(new { errors = result.Errors });
        }

        return Ok(new { mfaId = result.MfaId });
    }

    [HttpPost("verify")]
    [Authorize]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpApiRequest request)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(request.MfaId)
            || string.IsNullOrWhiteSpace(request.VerificationCode))
        {
            return BadRequest(new { error = "invalid_request", error_description = "mfaId and verificationCode are required" });
        }

        var result = await _mfaManagementService.VerifyOTPAsync(new VerifyOtpRequest
        {
            MfaId = request.MfaId,
            VerificationCode = request.VerificationCode,
            AuthType = request.AuthType,
            IsFromTokenCall = request.IsFromTokenCall
        });

        if (!result.IsValid)
        {
            return BadRequest(new { valid = false, errors = result.Errors });
        }

        return Ok(new { valid = true, userId = result.UserId });
    }

    // Self-service: a user switches their own preferred MFA method. Own-identity enforced via GetCurrentUserId().
    [HttpPut("method")]
    [Authorize]
    public async Task<IActionResult> SetMfaMethod([FromBody] SetMfaMethodRequest request)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId) || request == null)
        {
            return BadRequest(new { error = "invalid_request" });
        }

        var user = await _authenticationRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var previousType = user.UserMfaType;

        if (request.MfaType == UserMfaType.Email)
        {
            if (!user.EmailVerifiedAtUtc.HasValue)
            {
                return BadRequest(new { error = "email_not_verified", error_description = "Email must be verified before enabling Email OTP MFA" });
            }

            await _authenticationRepository.UpdatePartialAsync<UserEntity>(userId, new Dictionary<string, object>
            {
                { nameof(UserEntity.MfaEnabled), true },
                { nameof(UserEntity.UserMfaType), UserMfaType.Email },
                { nameof(UserEntity.IsMfaVerified), true },
                { nameof(UserEntity.LastUpdatedDate), DateTime.UtcNow },
                { nameof(UserEntity.LastUpdatedBy), userId }
            });

            if (previousType != UserMfaType.Email)
            {
                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = userId,
                    Category = UserActivityCategory.Auth,
                    Event = LoginAuditEvents.MfaEnrollmentCompleted,
                    Source = "auth-mfa-enrollment",
                    Outcome = "success",
                    Metadata = new Dictionary<string, string>
                    {
                        { "method", UserMfaType.Email.ToString() },
                        { "previous", previousType.ToString() }
                    }
                });
            }

            return Ok(new { enabled = true, method = UserMfaType.Email.ToString() });
        }

        if (request.MfaType == UserMfaType.TOTP)
        {
            if (!user.MfaEnabled)
            {
                return BadRequest(new { error = "mfa_not_enrolled", error_description = "Complete TOTP setup before choosing TOTP as preferred method" });
            }

            await _authenticationRepository.UpdatePartialAsync<UserEntity>(userId, new Dictionary<string, object>
            {
                { nameof(UserEntity.UserMfaType), UserMfaType.TOTP },
                { nameof(UserEntity.LastUpdatedDate), DateTime.UtcNow },
                { nameof(UserEntity.LastUpdatedBy), userId }
            });

            if (previousType != UserMfaType.TOTP)
            {
                await AuditUserEventAsync(LoginAuditEvents.MfaMethodChanged, userId, UserMfaType.TOTP,
                    new Dictionary<string, string> { { "previous", previousType.ToString() }, { "new", UserMfaType.TOTP.ToString() } });
            }

            return Ok(new { enabled = true, method = UserMfaType.TOTP.ToString() });
        }

        var result = await _mfaManagementService.DisableUserMfa(new DisableUserMfaRequest { UserId = userId });
        if (result.Errors != null && result.Errors.Count > 0)
        {
            return BadRequest(new { errors = result.Errors });
        }

        if (previousType != UserMfaType.None)
        {
            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = userId,
                Category = UserActivityCategory.Auth,
                Event = LoginAuditEvents.MfaRemoved,
                Source = "auth-mfa-enrollment",
                Outcome = "success",
                Metadata = new Dictionary<string, string>
                {
                    { "previous", previousType.ToString() }
                }
            });
        }

        return Ok(new { enabled = false, method = UserMfaType.None.ToString() });
    }

    // Self-service: a user disables their own MFA. Own-identity enforced via GetCurrentUserId().
    [HttpPost("disable")]
    [Authorize]
    public async Task<IActionResult> DisableMfa()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await _mfaManagementService.DisableUserMfa(new DisableUserMfaRequest { UserId = userId });
        if (result.Errors != null && result.Errors.Count > 0)
        {
            return BadRequest(new { errors = result.Errors });
        }

        await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
        {
            UserId = userId,
            Category = UserActivityCategory.Auth,
            Event = LoginAuditEvents.MfaRemoved,
            Source = "auth-mfa-enrollment",
            Outcome = "success"
        });
        return Ok(new { disabled = true });
    }

    [HttpGet("backup-codes")]
    [Authorize]
    public async Task<IActionResult> GetBackupCodesStatus()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var count = await _backupCodeService.GetRemainingCountAsync(userId);
        return Ok(new { remaining = count });
    }

    [HttpPost("backup-codes/generate")]
    [Authorize]
    public async Task<IActionResult> GenerateBackupCodes()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var config = await _mfaConfigurationService.GetAsync() ?? new Configuration();
        if (!config.AllowBackupCodes)
        {
            return BadRequest(new { error = "backup_codes_disabled" });
        }

        var user = await _authenticationRepository.GetUserByIdAsync(userId);
        if (user == null || !user.MfaEnabled)
        {
            return BadRequest(new { error = "mfa_not_enrolled", error_description = "Enroll in MFA before generating backup codes" });
        }

        var result = await _backupCodeService.GenerateAsync(userId, config.BackupCodesCount);
        if (!result.IsSuccess)
        {
            return BadRequest(new { errors = result.Errors });
        }

        return Ok(new { codes = result.PlainCodes });
    }

    [HttpPost("backup-codes/use")]
    [AllowAnonymous]
    public async Task<IActionResult> ConsumeBackupCode([FromBody] ConsumeBackupCodeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "invalid_request" });
        }

        var result = await _backupCodeService.ConsumeAsync(request.UserId, request.Code);
        if (!result.IsValid)
        {
            return BadRequest(new { error = "invalid_backup_code", errors = result.Errors });
        }
        return Ok(new { valid = true });
    }

    private string? GetCurrentUserId()
    {
        return BlocksContext.GetContext()?.UserId
            ?? User?.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value;
    }

    private async Task AuditPolicyAsync(string eventType, UpdateMfaPolicyRequest request)
    {
        await _auditService.WriteAsync(new MfaAuditEvent
        {
            EventType = eventType,
            UserId = GetCurrentUserId(),
            Status = "success",
            Details = $"enableMfa={request.EnableMfa},requireAll={request.RequireMfaForAllUsers}"
        });
    }

    private Task AuditUserEventAsync(string eventType, string userId, UserMfaType? mfaType, Dictionary<string, string> details)
    {
        return _auditService.WriteAsync(new MfaAuditEvent
        {
            EventType = eventType,
            UserId = userId,
            MfaType = mfaType,
            Status = "success",
            Details = string.Join(",", details.Select(kv => $"{kv.Key}={kv.Value}"))
        });
    }
}

public class UpdateMfaPolicyRequest
{
    public bool? EnableMfa { get; set; }
    public List<UserMfaType>? UserMfaType { get; set; }
    public MfaTemplate? MfaTemplate { get; set; }
    public bool? RequireMfaForAllUsers { get; set; }
    public List<string>? MfaRequiredRoles { get; set; }
    public List<string>? MfaExemptRoles { get; set; }
    public bool? AllowUserOptOut { get; set; }
    public bool? AllowBackupCodes { get; set; }
    public int? BackupCodesCount { get; set; }
}

public class VerifyTotpSetupRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public class GenerateOtpRequest
{
    [JsonPropertyName("mfaType")]
    public UserMfaType? MfaType { get; set; }

    [JsonPropertyName("sendPhoneNumberAsEmailDomain")]
    public string? SendPhoneNumberAsEmailDomain { get; set; }
}

public class ResendOtpApiRequest
{
    [JsonPropertyName("mfaId")]
    public string MfaId { get; set; } = string.Empty;

    [JsonPropertyName("sendPhoneNumberAsEmailDomain")]
    public string? SendPhoneNumberAsEmailDomain { get; set; }
}

public class VerifyOtpApiRequest
{
    [JsonPropertyName("mfaId")]
    public string MfaId { get; set; } = string.Empty;

    [JsonPropertyName("verificationCode")]
    public string VerificationCode { get; set; } = string.Empty;

    [JsonPropertyName("authType")]
    public UserMfaType AuthType { get; set; }

    [JsonPropertyName("isFromTokenCall")]
    public bool IsFromTokenCall { get; set; }
}

public class SetMfaMethodRequest
{
    [JsonPropertyName("mfaType")]
    public UserMfaType MfaType { get; set; }
}

public class ConsumeBackupCodeRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}
