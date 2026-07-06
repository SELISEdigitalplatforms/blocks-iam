using System.Text.Json.Serialization;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserEntity = Iam.DomainService.Entities.User;

namespace Api.Controllers;

[ApiController]
[Route("api/mfa")]
[Authorize]
public class MfaController : ControllerBase
{
    private readonly IMfaManagementService _mfaManagementService;
    private readonly IMfaConfigurationService _mfaConfigurationService;
    private readonly IMfaBackupCodeService _backupCodeService;
    private readonly IMfaAuditService _auditService;
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly TotpService _totpService;

    public MfaController(
        IMfaManagementService mfaManagementService,
        IMfaConfigurationService mfaConfigurationService,
        IMfaBackupCodeService backupCodeService,
        IMfaAuditService auditService,
        IAuthenticationRepository authenticationRepository,
        TotpService totpService)
    {
        _mfaManagementService = mfaManagementService;
        _mfaConfigurationService = mfaConfigurationService;
        _backupCodeService = backupCodeService;
        _auditService = auditService;
        _authenticationRepository = authenticationRepository;
        _totpService = totpService;
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
                await AuditUserEventAsync(LoginAuditEvents.MfaMethodChanged, userId, UserMfaType.Email,
                    new Dictionary<string, string> { { "previous", previousType.ToString() }, { "new", UserMfaType.Email.ToString() } });
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
            await AuditUserEventAsync(LoginAuditEvents.MfaMethodChanged, userId, UserMfaType.None,
                new Dictionary<string, string> { { "previous", previousType.ToString() }, { "new", UserMfaType.None.ToString() } });
        }

        return Ok(new { enabled = false, method = UserMfaType.None.ToString() });
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
