using System.Text.Json.Serialization;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
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
using Blocks.Genesis.Entities.Iam;
using UserEntity = Blocks.Genesis.Entities.Iam.User;

namespace Api.Controllers;

[ApiController]
[Route("mfa")]
// Deprecated: use "mfa". "api/mfa" kept as a temporary compatibility alias.

public class MfaController : ControllerBase
{
    private readonly IMfaManagementService _mfaManagementService;
    private readonly IMfaConfigurationService _mfaConfigurationService;
    private readonly IMfaAuditService _auditService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly TotpService _totpService;
    private readonly IUserActivityDispatcher _userActivityDispatcher;
    private readonly IMfaDisableService _mfaDisableService;
    private readonly IMfaAttemptGate _mfaAttemptGate;

    private const string InvalidRequestError = "invalid_request";

    public MfaController(
        IMfaManagementService mfaManagementService,
        IMfaConfigurationService mfaConfigurationService,
        IMfaAuditService auditService,
        IAuthenticationRepository authenticationRepository,
        TotpService totpService,
        IUserActivityDispatcher userActivityDispatcher,
        IMfaDisableService mfaDisableService,
        IMfaAttemptGate mfaAttemptGate)
    {
        _mfaAttemptGate = mfaAttemptGate;
        _mfaManagementService = mfaManagementService;
        _mfaConfigurationService = mfaConfigurationService;
        _auditService = auditService;
        _authenticationRepository = authenticationRepository;
        _totpService = totpService;
        _userActivityDispatcher = userActivityDispatcher;
        _mfaDisableService = mfaDisableService;
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
            return BadRequest(new { error = InvalidRequestError, error_description = "userId and code are required" });
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

    // Anonymous: this is invoked during the login MFA step, before the user holds an access
    // token. The live mfa_id (a short-lived, single-challenge Redis key) is the authorization
    // — possession of a valid one proves a challenge is in flight, the same model used by
    // the login verify itself.
    [HttpPost("resend")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpApiRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MfaId))
        {
            return BadRequest(new { error = InvalidRequestError, error_description = "mfaId is required" });
        }

        var result = await _mfaManagementService.ResendOtpAsync(request.MfaId, request.SendPhoneNumberAsEmailDomain ?? string.Empty);

        if (result.Errors != null && result.Errors.Count > 0)
        {
            // Cooldown is not a client error — signal it as a rate limit so the FE can drive its
            // countdown from retry_after_seconds.
            if (result.Errors.ContainsKey("message") && result.Errors["message"] == "resend_too_soon")
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { errors = result.Errors });
            }

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
            return BadRequest(new { error = InvalidRequestError, error_description = "mfaId and verificationCode are required" });
        }

        var callerId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(callerId))
        {
            return Unauthorized();
        }

        // A caller may only verify a challenge issued to themselves. Someone else's id gets the
        // same answer as an unknown id, so ids cannot be probed.
        var owner = await _mfaManagementService.GetChallengeOwnerAsync(request.MfaId);
        if (!string.Equals(owner, callerId, StringComparison.Ordinal))
        {
            return BadRequest(new { valid = false, errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } } });
        }

        var caller = await _authenticationRepository.GetUserByIdAsync(callerId);
        if (caller == null)
        {
            return Unauthorized();
        }

        if (IsLocked(caller))
        {
            return LockedResult();
        }

        // Same counter and lockout settings the login mfa step uses, counted before the check.
        var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
        OtpVerificationResponse? result = null;
        var attempt = await _mfaAttemptGate.CheckAsync(callerId, authConfiguration, async () =>
        {
            result = await _mfaManagementService.VerifyOTPAsync(new VerifyOtpRequest
            {
                MfaId = request.MfaId,
                VerificationCode = request.VerificationCode,
                AuthType = request.AuthType,
                IsFromTokenCall = request.IsFromTokenCall
            });
            return result.IsValid;
        });

        if (attempt.IsLocked)
        {
            return LockedResult();
        }

        if (!attempt.IsValid || result == null)
        {
            return BadRequest(new { valid = false, errors = result?.Errors });
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
            return BadRequest(new { error = InvalidRequestError });
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
            // TOTP becomes the method only through totp/setup + totp/verify-setup, which prove the
            // user holds the new authenticator.
            if (!user.MfaEnabled || previousType != UserMfaType.TOTP)
            {
                return BadRequest(new { error = "mfa_not_enrolled", error_description = "Complete TOTP setup before choosing TOTP as preferred method" });
            }

            return Ok(new { enabled = true, method = UserMfaType.TOTP.ToString() });
        }

        var disabled = await DisableWithProofAsync(userId, new MfaDisableRequest
        {
            Password = request.Password,
            MfaId = request.MfaId,
            VerificationCode = request.VerificationCode
        });
        if (disabled != null)
        {
            return disabled;
        }

        return Ok(new { enabled = false, method = UserMfaType.None.ToString() });
    }

    // Self-service: a user disables their own MFA. Own-identity enforced via GetCurrentUserId();
    // the password and a fresh code of the current method are required as well.
    [HttpPost("disable")]
    [Authorize]
    public async Task<IActionResult> DisableMfa([FromBody] MfaDisableRequest? request)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        return await DisableWithProofAsync(userId, request ?? new MfaDisableRequest()) ?? Ok(new { disabled = true });
    }

    // Null when MFA was turned off; otherwise the error to return.
    private async Task<IActionResult?> DisableWithProofAsync(string userId, MfaDisableRequest request)
    {
        var result = await _mfaDisableService.DisableAsync(userId, request);
        if (!result.Succeeded)
        {
            return StatusCode(result.StatusCode, new { error = result.Error, error_description = result.ErrorDescription });
        }

        await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
        {
            UserId = userId,
            Category = UserActivityCategory.Auth,
            Event = LoginAuditEvents.MfaRemoved,
            Source = "auth-mfa-enrollment",
            Outcome = "success",
            Metadata = new Dictionary<string, string> { { "previous", result.PreviousMethod.ToString() } }
        });
        return null;
    }

    private static bool IsLocked(UserEntity user) =>
        user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow;

    private static ObjectResult LockedResult() =>
        new(new { error = OAuthError.AccountLocked, error_description = "Account is temporarily locked due to failed authentication attempts" })
        {
            StatusCode = StatusCodes.Status423Locked
        };

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

    // Needed only to turn MFA off (MfaType None): same proof as POST mfa/disable.
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("mfaId")]
    public string? MfaId { get; set; }

    [JsonPropertyName("verificationCode")]
    public string? VerificationCode { get; set; }
}
