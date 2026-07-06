using System.Text.Json.Serialization;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UserEntity = Iam.DomainService.Entities.User;

namespace Api.Controllers;

[ApiController]
[Route("api/mfa")]
[Authorize]
public class MfaController : ControllerBase
{
    private readonly IMfaManagementService _mfaManagementService;
    private readonly IMfaConfigurationService _mfaConfigurationService;
    private readonly IMfaPolicyService _mfaPolicyService;
    private readonly IMfaBackupCodeService _backupCodeService;
    private readonly IMfaAuditService _auditService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly IUserRepository _userRepository;
    private readonly TotpService _totpService;
    private readonly ILogger<MfaController> _logger;

    public MfaController(
        IMfaManagementService mfaManagementService,
        IMfaConfigurationService mfaConfigurationService,
        IMfaPolicyService mfaPolicyService,
        IMfaBackupCodeService backupCodeService,
        IMfaAuditService auditService,
        IAuthenticationRepository authenticationRepository,
        IUserRepository userRepository,
        TotpService totpService,
        ILogger<MfaController> logger)
    {
        _mfaManagementService = mfaManagementService;
        _mfaConfigurationService = mfaConfigurationService;
        _mfaPolicyService = mfaPolicyService;
        _backupCodeService = backupCodeService;
        _auditService = auditService;
        _authenticationRepository = authenticationRepository;
        _userRepository = userRepository;
        _totpService = totpService;
        _logger = logger;
    }

    [HttpGet("config")]
    [Authorize]
    public async Task<IActionResult> GetPolicy()
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
    [Authorize]
    public async Task<IActionResult> UpdatePolicy([FromBody] UpdateMfaPolicyRequest request, CancellationToken ct)
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

    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetStatus()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _authenticationRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var policy = await _mfaPolicyService.EvaluateAsync(user, clientId: null);
        return Ok(new
        {
            enabled = user.MfaEnabled,
            preferredMethod = user.UserMfaType.ToString(),
            emailVerified = user.EmailVerifiedAtUtc.HasValue,
            phoneVerified = user.PhoneVerifiedAtUtc.HasValue,
            availableMethods = policy.AllowedMethods,
            mfaRequiredByPolicy = policy.Required,
            canUserDisable = policy.CanUserDisable,
            mustEnrollFirst = policy.MustEnrollFirst
        });
    }

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

        return Ok(new { enabled = true, method = "TOTP" });
    }

    [HttpPost("email/enable")]
    [Authorize]
    public async Task<IActionResult> EnableEmailMfa()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var user = await _authenticationRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

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

        return Ok(new { enabled = true, method = "Email" });
    }

    [HttpPut("preferred-method")]
    [Authorize]
    public async Task<IActionResult> SetPreferredMethod([FromBody] SetPreferredMfaMethodRequest request)
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

        if (request.MfaType == UserMfaType.Email && !user.EmailVerifiedAtUtc.HasValue)
        {
            return BadRequest(new { error = "email_not_verified" });
        }

        if (!user.MfaEnabled && request.MfaType != UserMfaType.None)
        {
            return BadRequest(new { error = "mfa_not_enrolled", error_description = "Enroll in MFA before changing preferred method" });
        }

        var previousType = user.UserMfaType;

        if (request.MfaType == UserMfaType.None)
        {
            await _mfaManagementService.DisableUserMfa(new DisableUserMfaRequest { UserId = userId });
            return Ok(new { enabled = false, method = "None" });
        }

        await _authenticationRepository.UpdatePartialAsync<UserEntity>(userId, new Dictionary<string, object>
        {
            { nameof(UserEntity.UserMfaType), request.MfaType },
            { nameof(UserEntity.LastUpdatedDate), DateTime.UtcNow },
            { nameof(UserEntity.LastUpdatedBy), userId }
        });

        if (previousType != request.MfaType)
        {
            await AuditUserEventAsync(LoginAuditEvents.MfaMethodChanged, userId, request.MfaType,
                new Dictionary<string, string> { { "previous", previousType.ToString() }, { "new", request.MfaType.ToString() } });
        }

        return Ok(new { enabled = true, method = request.MfaType.ToString() });
    }

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
        return Ok(new { disabled = true });
    }

    [HttpPost("admin/reset")]
    [Authorize]
    public async Task<IActionResult> AdminResetMfa([FromBody] AdminResetMfaRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "userId is required" });
        }

        var actorId = GetCurrentUserId();
        var result = await _mfaManagementService.DisableUserMfa(new DisableUserMfaRequest
        {
            UserId = request.UserId,
            AdminActorUserId = actorId,
            Reason = request.Reason
        });

        if (result.Errors != null && result.Errors.Count > 0)
        {
            return BadRequest(new { errors = result.Errors });
        }

        return Ok(new { reset = true });
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
            ?? User?.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
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

public class SetPreferredMfaMethodRequest
{
    [JsonPropertyName("mfaType")]
    public UserMfaType MfaType { get; set; }
}

public class AdminResetMfaRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class ConsumeBackupCodeRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}
