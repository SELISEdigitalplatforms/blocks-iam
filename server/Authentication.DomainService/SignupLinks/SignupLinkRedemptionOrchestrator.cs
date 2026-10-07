using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Accounts;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using StackExchange.Redis;

namespace Authentication.DomainService.SignupLinks;

/// <summary>
/// Phase 4 redemption matrix: Passwordless/PasswordRequired create, LinkUserReturned,
/// OrganizationJoined, ExistingUserRedirected, MFA-before-cookie, and activate cookie binding.
/// </summary>
public sealed class SignupLinkRedemptionOrchestrator : ISignupLinkRedemptionOrchestrator
{
    public const string SignupLinkValuePrefix = "signup-link:";
    public const string SignupLinkMailPurpose = "SignupLink";
    private const string MfaCachePrefix = "signup_link_mfa:";
    private const string RejectionNotFound = "not_found";
    private const string RejectionExhausted = "exhausted";
    private const string RejectionPasswordNotSet = "password_not_set";
    private const string RejectionInvalidPassword = "invalid_password";
    private const string RejectionAccountLocked = "account_locked";
    private const string RejectionCaptchaInvalid = "captcha_invalid";
    private const string RejectionAttemptsExceeded = "redemption_attempts_exceeded";
    private const string AuthenticationRequiredError = "authentication_required";
    private const string BranchPreExisting = "PreExisting";
    private const string BranchLinkUserReturned = "LinkUserReturned";

    private readonly ISignupLinkRepository _links;
    private readonly ISignupLinkRedemptionRepository _redemptions;
    private readonly ILinkSessionRepository _linkSessions;
    private readonly IOidcClientRegistrationLookup _oidcLookup;
    private readonly IAuthenticationRepository _authentication;
    private readonly IUserRepository _userRepository;
    private readonly IUserManagementMutationService _userMutation;
    private readonly IIdentityAccessManagementRepository _iamRepository;
    private readonly ICacheClient _cacheClient;
    private readonly ITenants _tenants;
    private readonly IConfiguration _configuration;
    private readonly IMfaChallengeIssuer _mfaChallengeIssuer;
    private readonly ISignupLinkEmbeddedTokenIssuer _embeddedTokens;
    private readonly IUserActivityDispatcher _userActivity;
    private readonly IPasswordCredentialVerifier? _passwordVerifier;
    private readonly ILogger<SignupLinkRedemptionCollaborators> _logger;

    public SignupLinkRedemptionOrchestrator(
        SignupLinkRedemptionStores stores,
        SignupLinkRedemptionCollaborators collaborators)
    {
        _links = stores.Links;
        _redemptions = stores.Redemptions;
        _linkSessions = stores.LinkSessions;
        _userRepository = stores.Users;
        _iamRepository = stores.Iam;
        _oidcLookup = collaborators.Oidc;
        _authentication = collaborators.Authentication;
        _userMutation = collaborators.UserMutation;
        _cacheClient = collaborators.Cache;
        _tenants = collaborators.Tenants;
        _configuration = collaborators.Configuration;
        _mfaChallengeIssuer = collaborators.Mfa;
        _embeddedTokens = collaborators.EmbeddedTokens;
        _userActivity = collaborators.UserActivity;
        _passwordVerifier = collaborators.PasswordVerifier;
        _logger = collaborators.Logger;
    }

    public async Task<IActionResult> RedeemAsync(
        string? code,
        string? tenantIdHint,
        HttpRequest request,
        HttpResponse response)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("Signup link redeem rejected: the request carried no code");
            return InvalidLink();
        }

        var hash = SignupLinkCodeHasher.Hash(code);
        var link = await _links.GetByCodeHashAsync(hash);
        if (link == null)
        {
            // A miss has no link to hang a SignupLinkRedemptions record on, so this line is its
            // only trace. The hash rather than the code, so the log cannot be replayed; it is
            // the value SignupLinks.CodeHash holds.
            _logger.LogWarning(
                "Signup link redeem rejected: no link matches code hash {CodeHash} (tenant hint {TenantIdHint}). The code is wrong, or the link was generated in another tenant",
                hash,
                tenantIdHint);
            return InvalidLink();
        }

        var gate = await RejectIfLinkNotRedeemableAsync(link, tenantIdHint, request);
        if (gate != null)
        {
            return gate;
        }

        var existing = await _userRepository.GetUserByEmailAsync(link.Email);

        // Branch 2 first — PasswordRequired resume may use a Redeemed link (MaxRedemptions=1).
        if (existing != null
            && !string.IsNullOrWhiteSpace(link.CreatedUserId)
            && string.Equals(link.CreatedUserId, existing.ItemId, StringComparison.Ordinal))
        {
            if (IsUnusableAccount(existing, allowPendingVerification: true))
            {
                await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1, userId: existing.ItemId,
                    detail: $"the account this link created is unusable: {DescribeUnusableAccount(existing)}");
                return InvalidLink();
            }

            return await HandleLinkUserReturnedAsync(link, existing, request, response);
        }

        // A pending account -- invited, or left by another link, and never activated -- is
        // usable here: redeeming the link proves what the activation email would have, so
        // HandlePreExistingAsync activates it. Locked, suspended and deactivated stay refused.
        if (IsUnusableAccount(existing, allowPendingVerification: true))
        {
            await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1, userId: existing!.ItemId,
                detail: $"an account already exists for the invited email and is unusable: {DescribeUnusableAccount(existing)}");
            return InvalidLink();
        }

        var rejection = ClassifyExhaustion(link);
        if (rejection != null)
        {
            await RecordRejectionAsync(link, rejection, request, ordinal: link.RedemptionCount + 1,
                detail: rejection == RejectionExhausted
                    ? $"the link has no redemptions left (status {link.Status}, redeemed {link.RedemptionCount} of {DescribeMaxRedemptions(link)})"
                    : $"the link status is {link.Status}, not Active");
            return InvalidLink();
        }

        if (existing != null)
        {
            if (link.RequireExistingUserPassword && !IsNeverActivated(existing))
            {
                return await RequirePasswordForPreExistingAsync(link, existing, request);
            }

            return await HandlePreExistingAsync(link, existing, request, response);
        }

        if (link.CredentialMode == SignupLinkCredentialMode.PasswordRequired)
        {
            return await HandlePasswordRequiredNewAsync(link, request);
        }

        return await HandlePasswordlessNewAsync(link, request, response);
    }


    /// <summary>
    /// Pre-existing branch with a password-requiring link: an active account confirms its own
    /// password before anything is granted or consumed. An account with no password cannot,
    /// so it is told so -- the link already names the account, and the invitee needs to know
    /// to sign in normally first (spec A4).
    /// </summary>
    private async Task<IActionResult> RequirePasswordForPreExistingAsync(
        SignupLink link,
        User user,
        HttpRequest request)
    {
        if (string.IsNullOrEmpty(user.Password))
        {
            await RecordRejectionAsync(link, RejectionPasswordNotSet, request, ordinal: link.RedemptionCount + 1, userId: user.ItemId,
                detail: "the link requires the existing user's password and the account has none");
            return new BadRequestObjectResult(new { error = RejectionPasswordNotSet });
        }

        return await StartPasswordStepAsync(link, user, BranchPreExisting, request);
    }

    /// <summary>
    /// Answers redeem with a password step instead of a session. Nothing is granted, consumed
    /// or issued; the server keeps what it needs under a single-use id for five minutes.
    /// </summary>
    private async Task<IActionResult> StartPasswordStepAsync(
        SignupLink link,
        User user,
        string branch,
        HttpRequest request)
    {
        var redemptionId = Base64Url(RandomNumberGenerator.GetBytes(32));
        var ctx = new SignupLinkAuthContext
        {
            LinkId = link.ItemId,
            TenantId = link.TenantId,
            UserId = user.ItemId,
            Branch = branch,
            Attempts = 0,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(IdpConstants.OidcStateCacheTtlSeconds)
        };

        await _cacheClient.AddStringValueAsync(
            IdpConstants.SignupLinkAuthCachePrefix + redemptionId,
            JsonSerializer.Serialize(ctx),
            IdpConstants.OidcStateCacheTtlSeconds);

        await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.AuthenticationRequired, request, link.RedemptionCount, grant: false);
        _logger.LogInformation(
            "Signup link {LinkId} asked existing user {UserId} ({MaskedEmail}) to confirm their password ({Branch})",
            link.ItemId,
            user.ItemId,
            SignupLinkCodeHasher.MaskEmail(link.Email),
            branch);

        return new OkObjectResult(new RedeemSignupLinkResponse
        {
            Error = AuthenticationRequiredError,
            RedemptionId = redemptionId,
            MaskedEmail = SignupLinkCodeHasher.MaskEmail(link.Email),
            Mode = link.Mode.ToString()
        });
    }

    public async Task<IActionResult> AuthenticateAsync(
        string? redemptionId,
        string? password,
        string? captchaCode,
        string? tenantIdHint,
        HttpRequest request,
        HttpResponse response)
    {
        if (string.IsNullOrWhiteSpace(redemptionId) || string.IsNullOrEmpty(password))
        {
            return new BadRequestObjectResult(new { error = "invalid_request", error_description = "redemptionId and password are required" });
        }

        var key = IdpConstants.SignupLinkAuthCachePrefix + redemptionId;
        var ctx = await LoadAuthContextAsync(key);
        if (ctx == null
            || string.IsNullOrWhiteSpace(tenantIdHint)
            || !string.Equals(ctx.TenantId, tenantIdHint, StringComparison.OrdinalIgnoreCase))
        {
            // The id is never logged: it is the credential for this step.
            _logger.LogWarning("Signup link authenticate rejected: the redemption is unknown, expired, already used, or for another tenant (tenant hint {TenantIdHint})", tenantIdHint);
            return InvalidRedemption();
        }

        var (link, user, refusal) = await ReloadForPasswordStepAsync(ctx, request);
        if (refusal != null)
        {
            await _cacheClient.RemoveKeyAsync(key);
            return refusal;
        }

        if (_passwordVerifier == null)
        {
            _logger.LogError("Signup link authenticate cannot run: no password verifier is registered");
            return new ObjectResult(new { error = "server_error" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }

        var verification = await _passwordVerifier.VerifyAsync(user!, password, captchaCode, request, ctx.TenantId);
        if (!verification.Succeeded)
        {
            return await RefusePasswordAsync(link!, user!, ctx, key, verification, request);
        }

        // Exactly one caller gets past this point for a given id, however many raced here
        // with the right password (C7).
        if (!await TryClaimAsync(IdpConstants.SignupLinkAuthClaimCachePrefix + redemptionId))
        {
            return InvalidRedemption();
        }

        await _cacheClient.RemoveKeyAsync(key);

        if (user!.MfaEnabled)
        {
            return await StartMfaChallengeAsync(link!, user, pendingGrant: true, branch: ctx.Branch);
        }

        var finalizeRefusal = await FinalizeExistingUserAsync(link!, user, ctx.Branch, request);
        if (finalizeRefusal != null)
        {
            return finalizeRefusal;
        }

        return await CompleteRedemptionAsync(link!, user, request, response, ["link", "pwd"]);
    }

    private async Task<SignupLinkAuthContext?> LoadAuthContextAsync(string key)
    {
        var raw = await _cacheClient.GetStringValueAsync(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var ctx = JsonSerializer.Deserialize<SignupLinkAuthContext>(raw);
            if (ctx == null
                || string.IsNullOrWhiteSpace(ctx.LinkId)
                || string.IsNullOrWhiteSpace(ctx.UserId)
                || ctx.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return null;
            }

            return ctx;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-reads the link and the account the password step was issued for, so a link revoked,
    /// expired or spent in the meantime, or an account suspended or deactivated, stops here.
    /// A lockout is left to the verifier, which answers it with 423 as login does.
    /// </summary>
    private async Task<(SignupLink? Link, User? User, IActionResult? Refusal)> ReloadForPasswordStepAsync(
        SignupLinkAuthContext ctx,
        HttpRequest request)
    {
        var link = await _links.GetByIdAsync(ctx.LinkId, ctx.TenantId);
        if (link == null)
        {
            _logger.LogWarning("Signup link authenticate rejected: link {LinkId} no longer exists in tenant {TenantId}", ctx.LinkId, ctx.TenantId);
            return (null, null, InvalidLink());
        }

        var gate = await RejectIfLinkNotRedeemableAsync(link, ctx.TenantId, request);
        if (gate != null)
        {
            return (link, null, gate);
        }

        var refusal = await RejectIfExhaustedForBranchAsync(link, ctx.Branch, request, ctx.UserId);
        if (refusal != null)
        {
            return (link, null, refusal);
        }

        var user = await _userRepository.GetUserByIdAsync(ctx.UserId);
        if (user == null
            || !string.Equals(user.Email, link.Email, StringComparison.OrdinalIgnoreCase)
            || IsUnusableAccount(user, allowPendingVerification: false, ignoreLockout: true))
        {
            await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1, userId: ctx.UserId,
                detail: user == null
                    ? "the account the password step was issued for no longer exists"
                    : "the account the password step was issued for no longer matches the link or is unusable");
            return (link, null, InvalidLink());
        }

        return (link, user, null);
    }

    /// <summary>
    /// Only the pre-existing branch consumes the link, so only it can run out. The
    /// link-returned branch re-enters a link it already spent, as it does today.
    /// </summary>
    private async Task<IActionResult?> RejectIfExhaustedForBranchAsync(
        SignupLink link,
        string? branch,
        HttpRequest request,
        string userId)
    {
        if (string.Equals(branch, BranchLinkUserReturned, StringComparison.Ordinal))
        {
            return null;
        }

        var rejection = ClassifyExhaustion(link);
        if (rejection == null)
        {
            return null;
        }

        await RecordRejectionAsync(link, rejection, request, ordinal: link.RedemptionCount + 1, userId: userId,
            detail: $"the link ran out before the password step finished (status {link.Status}, redeemed {link.RedemptionCount} of {DescribeMaxRedemptions(link)})");
        return InvalidLink();
    }

    private async Task<IActionResult> RefusePasswordAsync(
        SignupLink link,
        User user,
        SignupLinkAuthContext ctx,
        string key,
        PasswordVerificationResult verification,
        HttpRequest request)
    {
        var ordinal = link.RedemptionCount + 1;

        if (verification.StatusCode == StatusCodes.Status423Locked || verification.AccountLockedNow)
        {
            await RecordRejectionAsync(link, RejectionAccountLocked, request, ordinal, user.ItemId, "the account is locked");
            return new ObjectResult(new
            {
                error = OAuth.OAuthError.AccountLocked,
                error_description = PasswordCredentialVerifier.LockedDescription
            })
            { StatusCode = StatusCodes.Status423Locked };
        }

        if (verification.CaptchaRequired)
        {
            if (string.Equals(verification.Error, OAuth.OAuthError.CaptchaInvalid, StringComparison.Ordinal))
            {
                await RecordRejectionAsync(link, RejectionCaptchaInvalid, request, ordinal, user.ItemId, "the CAPTCHA answer was invalid");
            }

            return new BadRequestObjectResult(new
            {
                error = verification.Error,
                error_description = verification.ErrorDescription,
                captcha_required = true,
                captcha_site_key = verification.CaptchaSiteKey
            });
        }

        if (!string.Equals(verification.Error, OAuth.OAuthError.InValidUseNamePassword, StringComparison.Ordinal))
        {
            return new ObjectResult(new { error = verification.Error, error_description = verification.ErrorDescription })
            {
                StatusCode = verification.StatusCode
            };
        }

        ctx.Attempts++;
        if (ctx.Attempts >= IdpConstants.SignupLinkAuthMaxAttempts)
        {
            await _cacheClient.RemoveKeyAsync(key);
            await RecordRejectionAsync(link, RejectionAttemptsExceeded, request, ordinal, user.ItemId,
                $"{ctx.Attempts} wrong passwords on one password step; the step was discarded");
        }
        else
        {
            var remaining = (long)Math.Ceiling((ctx.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);
            if (remaining > 0)
            {
                await _cacheClient.AddStringValueAsync(key, JsonSerializer.Serialize(ctx), remaining);
            }

            await RecordRejectionAsync(link, RejectionInvalidPassword, request, ordinal, user.ItemId, "the password was not correct");
        }

        // 400 rather than login's 401: the hosted page's HTTP client treats any 401 as an
        // expired session and navigates to /login, which would drop the invitee (spec A6).
        return new BadRequestObjectResult(new
        {
            error = OAuth.OAuthError.InValidUseNamePassword,
            error_description = verification.ErrorDescription
        });
    }

    /// <summary>
    /// What the pre-existing branch used to do before the session, run only once the password
    /// (and any OTP) has been confirmed. The link is consumed before the grant, so a link that
    /// ran out grants nothing.
    /// </summary>
    private async Task<IActionResult?> FinalizeExistingUserAsync(
        SignupLink link,
        User user,
        string? branch,
        HttpRequest request)
    {
        if (string.Equals(branch, BranchLinkUserReturned, StringComparison.Ordinal))
        {
            await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.LinkUserReturned, request, link.RedemptionCount, grant: false);
            return null;
        }

        var orgId = link.OrganizationId ?? string.Empty;
        var alreadyMember = OrganizationAccessResolver.HasOrganizationAccess(user, orgId);

        var consumed = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, createdUserId: string.Empty, DateTime.UtcNow);
        if (consumed == null)
        {
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: user.ItemId,
                detail: "a concurrent request took the last redemption while the existing user was confirming their password");
            return InvalidLink();
        }

        if (!alreadyMember)
        {
            GrantOrganization(user, orgId, link.Roles, link.Permissions);
            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = user.ItemId;
            await _userRepository.UpdateUserAsync(user);
        }

        await RecordSuccessAsync(
            link,
            user.ItemId,
            alreadyMember ? SignupLinkRedemptionOutcome.ExistingUserRedirected : SignupLinkRedemptionOutcome.OrganizationJoined,
            request,
            consumed.RedemptionCount,
            grant: !alreadyMember);
        return null;
    }

    /// <summary>
    /// The OTP leg of a password step: the grant waited for it, so it is applied here, once.
    /// </summary>
    private async Task<IActionResult> CompletePendingGrantMfaAsync(
        string mfaId,
        SignupLinkMfaContext ctx,
        User user,
        string method,
        HttpRequest request,
        HttpResponse response)
    {
        if (!await TryClaimAsync(IdpConstants.SignupLinkMfaClaimCachePrefix + mfaId))
        {
            return new BadRequestObjectResult(new { error = "invalid_mfa_session", error_description = "Mfa login session is expired or invalid" });
        }

        await _cacheClient.RemoveKeyAsync(MfaCachePrefix + mfaId);

        var link = await _links.GetByIdAsync(ctx.LinkId, ctx.TenantId);
        if (link == null)
        {
            _logger.LogWarning("Signup link MFA redeem rejected: link {LinkId} no longer exists in tenant {TenantId}", ctx.LinkId, ctx.TenantId);
            return InvalidLink();
        }

        var refusal = await RejectIfLinkNotRedeemableAsync(link, ctx.TenantId, request)
            ?? await RejectIfExhaustedForBranchAsync(link, ctx.Branch, request, user.ItemId)
            ?? await FinalizeExistingUserAsync(link, user, ctx.Branch, request);
        if (refusal != null)
        {
            return refusal;
        }

        return await CompleteRedemptionAsync(link, user, request, response, ["link", "pwd", method]);
    }

    /// <summary>SETNX. Fails closed: without a working claim the step must not proceed.</summary>
    private async Task<bool> TryClaimAsync(string key)
    {
        try
        {
            return await _cacheClient.CacheDatabase().StringSetAsync(
                key: key,
                value: "1",
                expiry: TimeSpan.FromSeconds(IdpConstants.OidcStateCacheTtlSeconds),
                keepTtl: false,
                when: When.NotExists,
                flags: CommandFlags.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signup link could not claim a single-use step; refusing it");
            return false;
        }
    }

    private static IActionResult InvalidRedemption() =>
        new BadRequestObjectResult(new { error = "invalid_redemption" });


    private async Task<IActionResult?> RejectIfLinkNotRedeemableAsync(
        SignupLink link,
        string? tenantIdHint,
        HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(tenantIdHint)
            && !string.Equals(link.TenantId, tenantIdHint, StringComparison.OrdinalIgnoreCase))
        {
            await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1,
                detail: $"the link belongs to tenant {link.TenantId} but the request named tenant {tenantIdHint}");
            return InvalidLink();
        }

        if (link.Status == SignupLinkStatus.Revoked)
        {
            await RecordRejectionAsync(link, "revoked", request, ordinal: link.RedemptionCount + 1,
                detail: "the link has been revoked");
            return InvalidLink();
        }

        if (link.ExpiresAtUtc <= DateTime.UtcNow || link.Status == SignupLinkStatus.Expired)
        {
            await RecordRejectionAsync(link, "expired", request, ordinal: link.RedemptionCount + 1,
                detail: $"the link expired at {link.ExpiresAtUtc:O} (status {link.Status})");
            return InvalidLink();
        }

        var (clientOk, clientRejection, clientDetail) = await ValidateClientAsync(link);
        if (!clientOk)
        {
            await RecordRejectionAsync(link, clientRejection!, request, ordinal: link.RedemptionCount + 1,
                detail: clientDetail);
            return InvalidLink();
        }

        return null;
    }

    public async Task<IActionResult> CompleteRedeemMfaAsync(
        string? mfaId,
        string? mfaCode,
        HttpRequest request,
        HttpResponse response)
    {
        if (string.IsNullOrWhiteSpace(mfaId) || string.IsNullOrWhiteSpace(mfaCode))
        {
            return new BadRequestObjectResult(new { error = "invalid_request", error_description = "mfa_id and mfa_code are required" });
        }

        var raw = await _cacheClient.GetStringValueAsync(MfaCachePrefix + mfaId);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BadRequestObjectResult(new { error = "invalid_mfa_session", error_description = "Mfa login session is expired or invalid" });
        }

        var ctx = JsonSerializer.Deserialize<SignupLinkMfaContext>(raw);
        if (ctx == null || string.IsNullOrWhiteSpace(ctx.UserId) || string.IsNullOrWhiteSpace(ctx.LinkId))
        {
            return new BadRequestObjectResult(new { error = "invalid_mfa_session" });
        }

        var user = await _userRepository.GetUserByIdAsync(ctx.UserId);
        if (user == null || IsUnusableAccount(user, allowPendingVerification: false))
        {
            _logger.LogWarning(
                "Signup link MFA redeem rejected for link {LinkId}: user {UserId} {Problem}",
                ctx.LinkId,
                ctx.UserId,
                user == null ? "no longer exists" : $"is unusable: {DescribeUnusableAccount(user)}");
            return InvalidLink();
        }

        var otpService = await _mfaChallengeIssuer.GetOtpServiceAsync(user);
        if (otpService == null)
        {
            return new ObjectResult(new { error = "server_error" }) { StatusCode = 500 };
        }

        var verification = await otpService.VerifyAsync(new VerifyOtpRequest
        {
            AuthType = user.UserMfaType,
            MfaId = mfaId,
            VerificationCode = mfaCode
        });

        if (!verification.IsValid)
        {
            return new BadRequestObjectResult(new { error = "invalid_mfa_code", error_description = "Invalid mfa code" });
        }

        var method = user.UserMfaType == UserMfaType.TOTP ? "totp" : "otp";
        if (ctx.PendingGrant)
        {
            return await CompletePendingGrantMfaAsync(mfaId, ctx, user, method, request, response);
        }

        await _cacheClient.RemoveKeyAsync(MfaCachePrefix + mfaId);

        var link = await _links.GetByIdAsync(ctx.LinkId, ctx.TenantId);
        if (link == null)
        {
            _logger.LogWarning(
                "Signup link MFA redeem rejected: link {LinkId} no longer exists in tenant {TenantId}",
                ctx.LinkId,
                ctx.TenantId);
            return InvalidLink();
        }

        var amr = new List<string> { "link", method };
        return await CompleteRedemptionAsync(link, user, request, response, amr);
    }

    public async Task<IActionResult?> CompleteActivationAsync(
        string? activationCode,
        HttpRequest request,
        HttpResponse response)
    {
        if (string.IsNullOrWhiteSpace(activationCode))
        {
            return null;
        }

        try
        {
            var keyMap = await _iamRepository.GetUserKeyMapByKeyAsync(activationCode);
            if (keyMap == null || string.IsNullOrWhiteSpace(keyMap.Value)
                || !keyMap.Value.StartsWith(SignupLinkValuePrefix, StringComparison.Ordinal))
            {
                // An ordinary invite or recovery: Value is a URL, so it can never carry the
                // prefix. This is the gate that keeps every non-signup-link activation on
                // exactly the behaviour it has today.
                return null;
            }

            var linkId = keyMap.Value[SignupLinkValuePrefix.Length..];
            if (string.IsNullOrWhiteSpace(linkId))
            {
                return null;
            }

            var user = await _userRepository.GetUserByIdAsync(keyMap.UserId);
            if (user == null)
            {
                return null;
            }

            // Link may be under the user's tenant; prefer keyMap-adjacent lookup via GetById with tenant from user context / link CreatedUserId.
            var tenantId = BlocksContext.GetContext()?.TenantId
                ?? request.Headers["X-Blocks-Key"].FirstOrDefault();

            SignupLink? link = null;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                link = await _links.GetByIdAsync(linkId, tenantId);
            }

            link ??= await _links.GetByItemIdAsync(linkId);
            if (link == null)
            {
                return null;
            }

            if (link.SignInAfterActivation)
            {
                // The invitee has just proven possession of a single-use activation key and
                // set the password in the same request. Minting here spares them signing in
                // with a password chosen seconds ago, and works for both modes -- unlike the
                // link-session cookie, which only an OIDC authorize request can spend.
                var result = await _embeddedTokens.IssueAsync(link, user, ["link"], request);
                return WithForwardedTo(result, link.ForwardedTo);
            }

            if (link.Mode == SignupLinkMode.Embedded)
            {
                // No authorize request will ever be made for an embedded link, so the link
                // session has nothing to spend it and the authorize URL has no client to
                // build from. Skip both rather than mint a cookie nobody reads.
                return null;
            }

            await IssueLinkSessionAsync(link, user.ItemId, request, response, new List<string> { "link" });
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to complete signup-link activation");
            return null;
        }
    }

    /// <summary>
    /// Adds the link's forwarded path to a token response, so the client knows where to land
    /// without a second call. Embedded redemption has no redirect to hang it on, which left
    /// the configured value unreachable for that mode.
    /// </summary>
    private static IActionResult WithForwardedTo(IActionResult result, string? forwardedTo)
    {
        if (string.IsNullOrWhiteSpace(forwardedTo)
            || result is not ObjectResult { Value: Dictionary<string, object?> payload } objectResult
            || (objectResult.StatusCode ?? 200) != 200)
        {
            return result;
        }

        var merged = new Dictionary<string, object?>(payload) { ["forwardedTo"] = forwardedTo };
        return new ObjectResult(merged) { StatusCode = objectResult.StatusCode };
    }


    private async Task<(bool Ok, string? Rejection, string? Detail)> ValidateClientAsync(SignupLink link)
    {
        // An embedded link has no client registration by construction, and never redirects,
        // so there is nothing here to check (SPEC26 C8).
        if (link.Mode == SignupLinkMode.Embedded)
        {
            return (true, null, null);
        }

        var clientOk = await _oidcLookup.GetByClientIdAsync(link.ClientId);
        if (clientOk == null)
        {
            return (false, "client_invalid", $"OIDC client {link.ClientId} is not registered");
        }

        if (!clientOk.IsActive)
        {
            return (false, "client_invalid", $"OIDC client {link.ClientId} is inactive");
        }

        if (!clientOk.RedirectUris.Any(u => string.Equals(u, link.RedirectUri, StringComparison.Ordinal)))
        {
            return (false, "client_invalid",
                $"redirect URI {link.RedirectUri} is not registered on OIDC client {link.ClientId} (registered: {string.Join(", ", clientOk.RedirectUris)}); the match is exact and case-sensitive");
        }

        // The redemption ends at /idp/callback, which resolves the provider mirroring this
        // client and exchanges the code with it. A client saved without
        // RegisterAsIdentityProvider has no such mirror, so the flow would run all the way to
        // a created account and a burned code before failing with invalid_provider. Reject it
        // here instead, while nothing has been written.
        var provider = await _authentication.GetIdentityProviderByClientIdAsync(link.ClientId);
        if (provider == null)
        {
            return (false, "provider_not_registered",
                $"OIDC client {link.ClientId} has no identity provider; it was saved without RegisterAsIdentityProvider");
        }

        if (!provider.IsActive)
        {
            return (false, "provider_not_registered",
                $"identity provider {provider.Provider} for OIDC client {link.ClientId} is inactive");
        }

        return (true, null, null);
    }

    private async Task<IActionResult> HandleLinkUserReturnedAsync(
        SignupLink link,
        User user,
        HttpRequest request,
        HttpResponse response)
    {
        if (link.CredentialMode == SignupLinkCredentialMode.PasswordRequired
            && user.Status == UserLifecycleStatus.PendingVerification)
        {
            var (key, expires) = await MintActivationKeyAsync(link, user);
            await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.LinkUserReturned, request, link.RedemptionCount, grant: false);
            return new OkObjectResult(new RedeemSignupLinkResponse
            {
                ActivationKey = key,
                ActivationKeyExpiresAtUtc = expires,
                CredentialMode = nameof(SignupLinkCredentialMode.PasswordRequired)
            });
        }

        // An account this link created that has since set a password is as much an existing
        // identity as any other, so it confirms that password too. One without a password --
        // a passwordless joiner -- keeps re-entering through the link until it expires.
        if (link.RequireExistingUserPassword
            && !IsNeverActivated(user)
            && !string.IsNullOrEmpty(user.Password))
        {
            return await StartPasswordStepAsync(link, user, BranchLinkUserReturned, request);
        }

        if (user.MfaEnabled)
        {
            return await StartMfaChallengeAsync(link, user);
        }

        var completion = await CompleteRedemptionAsync(link, user, request, response, new List<string> { "link" });
        await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.LinkUserReturned, request, link.RedemptionCount, grant: false);
        return completion;
    }

    /// <summary>
    /// An account that already existed when the link was generated.
    /// <para>
    /// The grant is applied and the link consumed first, then the session is issued -- so an
    /// invitee reaches the page the link names whether or not they had an account, which is
    /// the whole point of the link. Previously both outcomes handed back a login URL and left
    /// them to sign in, and for an embedded link that URL had no client to return them to.
    /// </para>
    /// </summary>
    private async Task<IActionResult> HandlePreExistingAsync(
        SignupLink link,
        User user,
        HttpRequest request,
        HttpResponse response)
    {
        var orgId = link.OrganizationId ?? string.Empty;
        var alreadyMember = OrganizationAccessResolver.HasOrganizationAccess(user, orgId);

        // Pending: invited, or left by another link, and never activated. The link stands in
        // for the activation email. A password-required link still has the invitee set one,
        // through the same activation key a link-created account gets.
        var pending = IsNeverActivated(user);
        if (pending && link.CredentialMode == SignupLinkCredentialMode.PasswordRequired)
        {
            return await HandlePendingPasswordRequiredAsync(link, user, alreadyMember, request);
        }

        if (pending)
        {
            ActivateByLink(user);
        }

        if (alreadyMember)
        {
            if (pending)
            {
                await _userRepository.UpdateUserAsync(user);
            }

            var updated = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, createdUserId: string.Empty, DateTime.UtcNow);
            if (updated == null)
            {
                // The budget ran out under a concurrent request. Issuing a session anyway would
                // let a spent link keep signing people in.
                await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: user.ItemId,
                    detail: "a concurrent request took the last redemption while the existing member was being redirected");
                return InvalidLink();
            }

            await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.ExistingUserRedirected, request, updated.RedemptionCount, grant: false);
            if (pending)
            {
                await AfterActivationByLinkAsync(link, user);
            }

            return await CompleteForExistingUserAsync(link, user, request, response, activatedByLink: pending);
        }

        // OrganizationJoined — grant in link org only; leave other orgs untouched. A pending
        // account's activation rides on the same write.
        GrantOrganization(user, orgId, link.Roles, link.Permissions);
        user.LastUpdatedDate = DateTime.UtcNow;
        user.LastUpdatedBy = user.ItemId;
        await _userRepository.UpdateUserAsync(user);

        var consumed = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, createdUserId: string.Empty, DateTime.UtcNow);
        if (consumed == null)
        {
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: user.ItemId,
                detail: "a concurrent request took the last redemption while the existing user was being joined to the organization");
            return InvalidLink();
        }

        await RecordSuccessAsync(
            link,
            user.ItemId,
            SignupLinkRedemptionOutcome.OrganizationJoined,
            request,
            consumed.RedemptionCount,
            grant: true);

        if (pending)
        {
            await AfterActivationByLinkAsync(link, user);
        }

        return await CompleteForExistingUserAsync(link, user, request, response, activatedByLink: pending);
    }

    /// <summary>
    /// A pending account on a password-required link: grant the organization and consume the
    /// link now, then hand back an activation key so the invitee sets a password. The account
    /// becomes active when that key is spent, through the ordinary activation endpoint, which
    /// also retires the invite's own key.
    /// </summary>
    private async Task<IActionResult> HandlePendingPasswordRequiredAsync(
        SignupLink link,
        User user,
        bool alreadyMember,
        HttpRequest request)
    {
        if (!alreadyMember)
        {
            GrantOrganization(user, link.OrganizationId ?? string.Empty, link.Roles, link.Permissions);
            user.LastUpdatedDate = DateTime.UtcNow;
            user.LastUpdatedBy = user.ItemId;
            await _userRepository.UpdateUserAsync(user);
        }

        // CreatedUserId is left unstamped: this link did not create the account, and stamping
        // it would let the account back in through the link-returned branch after the link is
        // spent.
        var consumed = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, createdUserId: string.Empty, DateTime.UtcNow);
        if (consumed == null)
        {
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: user.ItemId,
                detail: "a concurrent request took the last redemption while the pending account was being prepared for activation");
            return InvalidLink();
        }

        var (key, expires) = await MintActivationKeyAsync(link, user);
        await RecordSuccessAsync(
            link,
            user.ItemId,
            alreadyMember ? SignupLinkRedemptionOutcome.ExistingUserRedirected : SignupLinkRedemptionOutcome.OrganizationJoined,
            request,
            consumed.RedemptionCount,
            grant: !alreadyMember);

        return new OkObjectResult(new RedeemSignupLinkResponse
        {
            ActivationKey = key,
            ActivationKeyExpiresAtUtc = expires,
            CredentialMode = nameof(SignupLinkCredentialMode.PasswordRequired)
        });
    }

    /// <summary>
    /// Leaves a pending account in the state the activation email would, short of a password:
    /// a passwordless link sets none, exactly as for an account it creates.
    /// </summary>
    private static void ActivateByLink(User user)
    {
        var now = DateTime.UtcNow;
        user.Active = true;
        user.IsVerified = true;
        user.Status = UserLifecycleStatus.Active;
        user.StatusReason = "email_verified";
        user.EmailVerifiedAtUtc ??= now;
        // Self-activation: the invitee is the actor, as LastUpdatedBy below records.
        user.ActivatedAtUtc = now;
        user.ActivatedBy = user.ItemId;
        user.FailedLoginCount = 0;
        user.LastFailedLoginUtc = null;
        user.LockoutUntilUtc = null;
        user.LastUpdatedDate = now;
        user.LastUpdatedBy = user.ItemId;
    }

    /// <summary>
    /// What the activation email does once the account is active: retire its outstanding
    /// keys, so the invite email stops working, and record the activation. Neither is worth
    /// failing a sign-in the account is already committed to, so a failure is logged instead.
    /// </summary>
    private async Task AfterActivationByLinkAsync(SignupLink link, User user)
    {
        try
        {
            await AccountActivation.RetireActivationKeysAsync(_iamRepository, _cacheClient, user.ItemId);
            await _userActivity.SendUserActivityAsync(AccountActivation.ActivatedEvent(user.ItemId, "signup-link"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signup link {LinkId} activated user {UserId} but could not retire its activation keys or record the activation", link.ItemId, user.ItemId);
        }
    }

    /// <summary>
    /// The terminal step for an existing account: an MFA challenge when the user has one,
    /// otherwise the ordinary completion.
    /// <para>
    /// The MFA check is not optional here. This branch never issued a session before, so it
    /// never needed one; issuing without it would let an emailed link walk past a second
    /// factor. The grant and the link's consumption happen before this either way -- they are
    /// not a session, and an abandoned challenge should not leave the invitation half applied.
    /// </para>
    /// <para>
    /// The one exception is an account this redemption just activated whose factor is an
    /// authenticator app it never enrolled: there is no second factor to check yet, and the
    /// challenge could only fail. Ordinary login refuses such an account outright.
    /// </para>
    /// </summary>
    private async Task<IActionResult> CompleteForExistingUserAsync(
        SignupLink link,
        User user,
        HttpRequest request,
        HttpResponse response,
        bool activatedByLink = false)
    {
        var unenrolledAuthenticator = user.UserMfaType == UserMfaType.TOTP && !user.IsMfaVerified;
        if (user.MfaEnabled && !(activatedByLink && unenrolledAuthenticator))
        {
            return await StartMfaChallengeAsync(link, user);
        }

        return await CompleteRedemptionAsync(link, user, request, response, ["link"]);
    }

    private async Task<IActionResult> HandlePasswordRequiredNewAsync(
        SignupLink link,
        HttpRequest request)
    {
        var userId = await CreateSignupLinkUserAsync(link, passwordRequired: true);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError("Signup link redeem failed to create PendingVerification user for link {LinkId}", link.ItemId);
            return InvalidLink();
        }

        var now = DateTime.UtcNow;
        var updated = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, userId, now);
        if (updated == null)
        {
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: userId,
                detail: "a concurrent request took the last redemption after the user was created");
            return InvalidLink();
        }

        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            _logger.LogError("Signup link redeem could not read back PendingVerification user {UserId} for link {LinkId}", userId, link.ItemId);
            return InvalidLink();
        }

        var (key, expires) = await MintActivationKeyAsync(link, user);
        await RecordSuccessAsync(link, userId, SignupLinkRedemptionOutcome.UserCreated, request, updated.RedemptionCount, grant: true);

        return new OkObjectResult(new RedeemSignupLinkResponse
        {
            ActivationKey = key,
            ActivationKeyExpiresAtUtc = expires,
            CredentialMode = nameof(SignupLinkCredentialMode.PasswordRequired)
        });
    }

    private async Task<IActionResult> HandlePasswordlessNewAsync(
        SignupLink link,
        HttpRequest request,
        HttpResponse response)
    {
        var userId = await CreateSignupLinkUserAsync(link, passwordRequired: false);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError("Signup link redeem failed to create user for link {LinkId}", link.ItemId);
            return InvalidLink();
        }

        var now = DateTime.UtcNow;
        var updated = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, userId, now);
        if (updated == null)
        {
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: userId,
                detail: "a concurrent request took the last redemption after the user was created");
            return InvalidLink();
        }

        await RecordSuccessAsync(link, userId, SignupLinkRedemptionOutcome.UserCreated, request, updated.RedemptionCount, grant: true);

        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user != null && user.MfaEnabled)
        {
            return await StartMfaChallengeAsync(link, user);
        }

        var completionUser = await _userRepository.GetUserByIdAsync(userId);
        if (completionUser == null)
        {
            _logger.LogError("Signup link redeem could not read back user {UserId} for link {LinkId}", userId, link.ItemId);
            return InvalidLink();
        }

        return await CompleteRedemptionAsync(link, completionUser, request, response, new List<string> { "link" });
    }

    private async Task<IActionResult> StartMfaChallengeAsync(
        SignupLink link,
        User user,
        bool pendingGrant = false,
        string? branch = null)
    {
        var otpService = await _mfaChallengeIssuer.GetOtpServiceAsync(user);
        if (otpService == null)
        {
            return new ObjectResult(new { error = "server_error", error_description = "Mfa provider is not available" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        var challengeResponse = await otpService.GenerateAsync(new UserInfo
        {
            ItemId = user.ItemId,
            Email = user.Email,
            Language = user.Language ?? "en-US",
            PhoneNumber = user.PhoneNumber
        });

        if (challengeResponse == null || !challengeResponse.IsSuccess || string.IsNullOrWhiteSpace(challengeResponse.MfaId))
        {
            return new ObjectResult(new { error = "server_error", error_description = "Failed to generate mfa challenge" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        var ctx = new SignupLinkMfaContext
        {
            UserId = user.ItemId,
            LinkId = link.ItemId,
            TenantId = link.TenantId,
            ClientId = link.ClientId,
            RedirectUri = link.RedirectUri,
            PendingGrant = pendingGrant,
            Branch = branch
        };

        await _cacheClient.AddStringValueAsync(
            MfaCachePrefix + challengeResponse.MfaId,
            JsonSerializer.Serialize(ctx),
            IdpConstants.OidcStateCacheTtlSeconds);

        // No cookie until MFA completes (H4).
        return new OkObjectResult(new RedeemSignupLinkResponse
        {
            Error = OAuthError.MfaEnabled,
            MfaId = challengeResponse.MfaId,
            UserMfa = user.UserMfaType.ToString()
        });
    }

    private async Task<(string Key, DateTime ExpiresAtUtc)> MintActivationKeyAsync(SignupLink link, User user)
    {
        var config = await _userRepository.GetIamConfigurationAsync();
        var minutes = config?.ActivationUrlLifetimeInMinutes > 0
            ? config.ActivationUrlLifetimeInMinutes
            : 60 * 24;
        var key = Guid.NewGuid().ToString("n");
        var expires = DateTime.UtcNow.AddMinutes(minutes);

        await _cacheClient.AddStringValueAsync(key, user.ItemId, minutes * 60);
        await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
        {
            ItemId = Guid.NewGuid().ToString(),
            Key = key,
            UserId = user.ItemId,
            IssueDate = DateTime.UtcNow,
            ExpireDate = expires,
            Value = SignupLinkValuePrefix + link.ItemId,
            MailPurpose = SignupLinkMailPurpose,
            Activated = false
        });

        return (key, expires);
    }

    /// <summary>
    /// The one place a redemption turns into a session, so both modes stay in step on
    /// everything that precedes it.
    /// <para>
    /// OIDC mints the restricted link session and hands back an authorize URL for the browser
    /// to follow. Embedded issues tokens directly and redirects nowhere, because it has no
    /// registered redirect URI to validate a destination against.
    /// </para>
    /// </summary>
    private async Task<IActionResult> CompleteRedemptionAsync(
        SignupLink link,
        User user,
        HttpRequest request,
        HttpResponse response,
        List<string> amr)
    {
        if (link.Mode == SignupLinkMode.Embedded)
        {
            // The link exists to put someone on a particular page. OIDC carries the path on
            // the authorize redirect; embedded has no redirect, so without this the invitee
            // signs in and lands wherever the app defaults to.
            var issued = await _embeddedTokens.IssueAsync(link, user, amr, request);
            return WithForwardedTo(issued, link.ForwardedTo);
        }

        var authorizeUrl = await IssueLinkSessionAsync(link, user.ItemId, request, response, amr);
        return new OkObjectResult(new RedeemSignupLinkResponse
        {
            Mode = nameof(SignupLinkMode.Oidc),
            AuthorizeUrl = authorizeUrl
        });
    }

    private async Task<string> IssueLinkSessionAsync(
        SignupLink link,
        string userId,
        HttpRequest request,
        HttpResponse response,
        List<string> amr)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(IdpConstants.LinkSessionLifetimeMinutes);
        var session = new LinkSessionModel
        {
            UserId = userId,
            TenantId = link.TenantId,
            OrganizationId = link.OrganizationId ?? string.Empty,
            ClientId = link.ClientId,
            LinkId = link.ItemId,
            Roles = link.Roles?.ToList() ?? [],
            Permissions = link.Permissions?.ToList() ?? [],
            Amr = amr,
            IssuedAtUtc = now,
            ExpiresAtUtc = expires
        };
        await _linkSessions.CreateAsync(session);

        var tenant = _tenants.GetTenantByID(link.TenantId);
        LinkSessionCookie.Append(request, response, tenant, link.TenantId, session.SessionId, expires);
        return await BuildAuthorizeUrlAsync(link);
    }

    private static void GrantOrganization(User user, string organizationId, List<string>? roles, List<string>? permissions)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return;
        }

        if (!user.OrganizationIds.Contains(organizationId))
        {
            user.OrganizationIds.Add(organizationId);
        }

        user.Roles[organizationId] = roles?.Count > 0
            ? roles
            : user.Roles.GetValueOrDefault(organizationId, []);

        user.Permissions[organizationId] = permissions?.Count > 0
            ? permissions
            : user.Permissions.GetValueOrDefault(organizationId, []);
    }

    private static string? ClassifyExhaustion(SignupLink link)
    {
        // Status is still consulted: a link redeemed before multi-use existed carries
        // Redeemed with a count that already meets its cap, and both say the same thing.
        if (link.Status is SignupLinkStatus.Redeemed or SignupLinkStatus.Exhausted
            || !SignupLink.HasRedemptionBudget(
                link.RedemptionCount, link.MaxRedemptions))
        {
            return RejectionExhausted;
        }

        if (link.Status != SignupLinkStatus.Active)
        {
            return RejectionNotFound;
        }

        return null;
    }

    private static bool IsUnusableAccount(User? user, bool allowPendingVerification, bool ignoreLockout = false)
    {
        if (user == null)
        {
            return false;
        }

        if (!ignoreLockout && user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
        {
            return true;
        }

        if (user.Status is UserLifecycleStatus.Suspended)
        {
            return true;
        }

        if (user.DeactivatedAtUtc.HasValue)
        {
            return true;
        }

        if (allowPendingVerification && IsNeverActivated(user))
        {
            return false;
        }

        if (!user.Active || user.Status != UserLifecycleStatus.Active)
        {
            // PendingVerification without allow flag, or other non-active states.
            if (user.Status == UserLifecycleStatus.PendingVerification)
            {
                return true;
            }

            return !user.Active;
        }

        return false;
    }

    /// <summary>
    /// Created and never activated. Status alone does not say so: an invite whose
    /// VerifiedType is not None -- Email is the default -- is stored with Status Active and
    /// Active false, and so are documents written before Status existed. This is the rule
    /// the user list applies, so an account it shows as pending is one a link will activate.
    /// An inactive account that was ever verified reads as deactivated and stays refused.
    /// </summary>
    private static bool IsNeverActivated(User user) =>
        UserAccountStates.Resolve(user.Active, user.Status, user.IsVerified) == UserAccountState.PendingVerification;

    /// <summary>
    /// Why <see cref="IsUnusableAccount"/> refused an account, checked in the same order.
    /// </summary>
    private static string DescribeUnusableAccount(User user)
    {
        if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
        {
            return $"locked out until {user.LockoutUntilUtc.Value:O}";
        }

        if (user.Status is UserLifecycleStatus.Suspended)
        {
            return "suspended";
        }

        if (user.DeactivatedAtUtc.HasValue)
        {
            return $"deactivated at {user.DeactivatedAtUtc.Value:O}";
        }

        if (user.Status == UserLifecycleStatus.PendingVerification)
        {
            return "pending verification";
        }

        return $"inactive (status {user.Status})";
    }

    private static string DescribeMaxRedemptions(SignupLink link) =>
        link.MaxRedemptions == SignupLink.UnlimitedMaxRedemptions ? "unlimited" : link.MaxRedemptions.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> CreateSignupLinkUserAsync(SignupLink link, bool passwordRequired)
    {
        var previous = BlocksContext.GetContext();
        try
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: link.TenantId,
                roles: null,
                userId: "signup-link",
                impersonated: false,
                isAuthenticated: false,
                requestUri: "signup-link-redeem",
                organizationId: link.OrganizationId ?? IdpConstants.DefaultOrganizationId,
                permissions: null,
                expireOn: DateTime.UtcNow.AddMinutes(5),
                email: link.Email,
                userName: link.Email,
                phoneNumber: null,
                displayName: link.FirstName,
                oauthToken: null,
                originalTenantId: link.TenantId,
                impersonationSessionId: null,
                applicationDomain: null));

            var request = new CreateUserRequest
            {
                Email = link.Email,
                UserName = link.Email,
                FirstName = link.FirstName,
                LastName = link.LastName,
                Language = link.Language ?? "en-US",
                OrganizationId = link.OrganizationId ?? IdpConstants.DefaultOrganizationId,
                Roles = link.Roles?.ToList() ?? [],
                Permissions = link.Permissions?.ToList() ?? [],
                Password = string.Empty,
                UserPassType = passwordRequired ? UserPassType.Password : UserPassType.None,
                UserCreationType = UserCreationType.Api,
                VerifiedType = passwordRequired ? UserVerifiedType.None : UserVerifiedType.Email,
                AllowedLogInType = passwordRequired
                    ? new List<UserLogInType> { UserLogInType.Password, UserLogInType.AuthrizationCode }
                    : new List<UserLogInType> { UserLogInType.AuthrizationCode },
                MfaEnabled = false,
                UserMfaType = UserMfaType.None,
                MailPurpose = passwordRequired ? SignupLinkMailPurpose : "AccountActivation"
            };

            var user = _userMutation.MapUser(request);
            if (passwordRequired)
            {
                user.Active = false;
                user.IsVerified = false;
                user.Status = UserLifecycleStatus.PendingVerification;
                user.EmailVerifiedAtUtc = null;
            }
            else
            {
                user.Active = true;
                user.IsVerified = true;
                user.Status = UserLifecycleStatus.Active;
                user.EmailVerifiedAtUtc = DateTime.UtcNow;
            }

            user.ProvisioningSource = UserProvisioningSource.API;
            user.Password = string.Empty;

            await _userRepository.CreateUserAsync(user);
            return user.ItemId;
        }
        finally
        {
            if (previous != null)
            {
                BlocksContext.SetContext(previous);
            }
        }
    }

    /// <summary>
    /// Pre-seeds the OIDC flow context this redemption's authorize request will be completed
    /// against, then returns the authorize URL for the browser to follow.
    /// <para>
    /// The context is written under the same <c>idp_flow:{state}</c> key
    /// <c>GET /idp/initiate</c> uses, because the construct finishes a signup-link redemption
    /// at the same <c>GET /idp/callback</c> it finishes an ordinary login at. That endpoint
    /// resolves <c>provider</c> by name against the IdentityProvider collection, so the name
    /// stored here has to be the client's own — the value the RegisterAsIdentityProvider sync
    /// derives from the client display name, not a provider <em>type</em> such as
    /// <c>blocks-oidc</c>.
    /// </para>
    /// </summary>
    private async Task<string> BuildAuthorizeUrlAsync(SignupLink link)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));

        // Rejected in ValidateClientAsync before any account is touched, so by here a missing
        // provider means the client was unregistered mid-redemption.
        var provider = await _authentication.GetIdentityProviderByClientIdAsync(link.ClientId)
            ?? throw new InvalidOperationException(
                $"No identity provider is registered for signup-link client '{link.ClientId}'");

        // PKCE binds the authorization code to this redemption. Driven by the provider's own
        // flag so the pair matches what /idp/callback will present at the token endpoint:
        // it sends the verifier only when the context carries one, and the exchange validates
        // it only when the authorize request stored a challenge.
        var codeVerifier = provider.RequirePkce ? Base64Url(RandomNumberGenerator.GetBytes(32)) : null;

        var flowContext = new
        {
            state,
            nonce,
            codeVerifier,
            provider = provider.Provider,
            tenantId = link.TenantId,
            clientId = link.ClientId,
            redirectUri = link.RedirectUri,
            createdAt = DateTime.UtcNow,
            forwardedTo = link.ForwardedTo
        };

        await _cacheClient.AddStringValueAsync(
            $"idp_flow:{state}",
            JsonSerializer.Serialize(flowContext),
            IdpConstants.IdpFlowCacheTtlSeconds);

        var baseUrl = IamHelper.GetConfiguredIamBaseUrl(_configuration).TrimEnd('/');
        var query = new Dictionary<string, string>
        {
            ["client_id"] = link.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = link.RedirectUri,
            ["scope"] = IdpConstants.OpenIdProfileEmailScope + " offline_access",
            ["state"] = state,
            ["nonce"] = nonce,
            ["tenant_id"] = link.TenantId
        };

        if (codeVerifier != null)
        {
            query["code_challenge"] = CodeChallenge(codeVerifier);
            query["code_challenge_method"] = IdpConstants.PkceMethodS256;
        }

        var qs = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"{baseUrl}/api/oidc/authorize?{qs}";
    }

    private static string CodeChallenge(string codeVerifier) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier)));

    private async Task RecordSuccessAsync(
        SignupLink link,
        string userId,
        SignupLinkRedemptionOutcome outcome,
        HttpRequest request,
        int ordinal,
        bool grant)
    {
        var now = DateTime.UtcNow;
        await _redemptions.InsertAsync(new SignupLinkRedemption
        {
            ItemId = Guid.NewGuid().ToString("n"),
            LinkId = link.ItemId,
            TenantId = link.TenantId,
            Email = link.Email,
            UserId = userId,
            Outcome = outcome,
            RedemptionOrdinal = ordinal,
            RedeemedAtUtc = now,
            IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(request),
            UserAgent = request.Headers.UserAgent.ToString(),
            GrantedOrganizationId = grant ? link.OrganizationId : null,
            GrantedRoles = grant ? link.Roles?.ToList() ?? [] : [],
            GrantedPermissions = grant ? link.Permissions?.ToList() ?? [] : [],
            CreatedDate = now,
            CreatedBy = userId,
            LastUpdatedDate = now,
            LastUpdatedBy = userId
        });
    }

    private async Task RecordRejectionAsync(
        SignupLink link,
        string reason,
        HttpRequest request,
        int ordinal,
        string? userId = null,
        string? detail = null)
    {
        // Every rejection answers the caller with the same invalid-link body on purpose, so
        // this line and the SignupLinkRedemptions record are the only places the reason shows.
        _logger.LogWarning(
            "Signup link redeem rejected ({Reason}) for link {LinkId} in tenant {TenantId}, invitee {MaskedEmail}, user {UserId}: {Detail}",
            reason,
            link.ItemId,
            link.TenantId,
            SignupLinkCodeHasher.MaskEmail(link.Email),
            userId,
            detail ?? reason);

        try
        {
            var now = DateTime.UtcNow;
            await _redemptions.InsertAsync(new SignupLinkRedemption
            {
                ItemId = Guid.NewGuid().ToString("n"),
                LinkId = link.ItemId,
                TenantId = link.TenantId,
                Email = link.Email,
                UserId = userId,
                Outcome = SignupLinkRedemptionOutcome.Rejected,
                RejectionReason = reason,
                RedemptionOrdinal = ordinal,
                RedeemedAtUtc = now,
                IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(request),
                UserAgent = request.Headers.UserAgent.ToString(),
                CreatedDate = now,
                LastUpdatedDate = now
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record signup link rejection for link {LinkId}", link.ItemId);
        }
    }

    private static IActionResult InvalidLink() =>
        new BadRequestObjectResult(new RedeemSignupLinkErrorResponse());

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class SignupLinkMfaContext
    {
        public string UserId { get; set; } = string.Empty;
        public string LinkId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// Set when a password step preceded this challenge, so the grant and the link's
        /// consumption still wait for the OTP. False on contexts cached before this field
        /// existed, which complete exactly as they always did.
        /// </summary>
        public bool PendingGrant { get; set; }

        public string? Branch { get; set; }
    }

    private sealed class SignupLinkAuthContext
    {
        public string LinkId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Branch { get; set; } = BranchPreExisting;
        public int Attempts { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
