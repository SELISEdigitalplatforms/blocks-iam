using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
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
            return InvalidLink();
        }

        var hash = SignupLinkCodeHasher.Hash(code);
        var link = await _links.GetByCodeHashAsync(hash);
        if (link == null)
        {
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
                await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1, userId: existing.ItemId);
                return InvalidLink();
            }

            return await HandleLinkUserReturnedAsync(link, existing, request, response);
        }

        if (IsUnusableAccount(existing, allowPendingVerification: false))
        {
            await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1, userId: existing?.ItemId);
            return InvalidLink();
        }

        var rejection = ClassifyExhaustion(link);
        if (rejection != null)
        {
            await RecordRejectionAsync(link, rejection, request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        if (existing != null)
        {
            return await HandlePreExistingAsync(link, existing, request);
        }

        if (link.CredentialMode == SignupLinkCredentialMode.PasswordRequired)
        {
            return await HandlePasswordRequiredNewAsync(link, request);
        }

        return await HandlePasswordlessNewAsync(link, request, response);
    }


    private async Task<IActionResult?> RejectIfLinkNotRedeemableAsync(
        SignupLink link,
        string? tenantIdHint,
        HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(tenantIdHint)
            && !string.Equals(link.TenantId, tenantIdHint, StringComparison.OrdinalIgnoreCase))
        {
            await RecordRejectionAsync(link, RejectionNotFound, request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        if (link.Status == SignupLinkStatus.Revoked)
        {
            await RecordRejectionAsync(link, "revoked", request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        if (link.ExpiresAtUtc <= DateTime.UtcNow || link.Status == SignupLinkStatus.Expired)
        {
            await RecordRejectionAsync(link, "expired", request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        var (clientOk, clientRejection) = await ValidateClientAsync(link);
        if (!clientOk)
        {
            await RecordRejectionAsync(link, clientRejection!, request, ordinal: link.RedemptionCount + 1);
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

        await _cacheClient.RemoveKeyAsync(MfaCachePrefix + mfaId);

        var link = await _links.GetByIdAsync(ctx.LinkId, ctx.TenantId);
        if (link == null)
        {
            return InvalidLink();
        }

        var method = user.UserMfaType == UserMfaType.TOTP ? "totp" : "otp";
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


    private async Task<(bool Ok, string? Rejection)> ValidateClientAsync(SignupLink link)
    {
        // An embedded link has no client registration by construction, and never redirects,
        // so there is nothing here to check (SPEC26 C8).
        if (link.Mode == SignupLinkMode.Embedded)
        {
            return (true, null);
        }

        var clientOk = await _oidcLookup.GetByClientIdAsync(link.ClientId);
        if (clientOk == null || !clientOk.IsActive
            || !clientOk.RedirectUris.Any(u => string.Equals(u, link.RedirectUri, StringComparison.Ordinal)))
        {
            return (false, "client_invalid");
        }

        // The redemption ends at /idp/callback, which resolves the provider mirroring this
        // client and exchanges the code with it. A client saved without
        // RegisterAsIdentityProvider has no such mirror, so the flow would run all the way to
        // a created account and a burned code before failing with invalid_provider. Reject it
        // here instead, while nothing has been written.
        var provider = await _authentication.GetIdentityProviderByClientIdAsync(link.ClientId);
        if (provider == null || !provider.IsActive)
        {
            return (false, "provider_not_registered");
        }

        return (true, null);
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

        if (user.MfaEnabled)
        {
            return await StartMfaChallengeAsync(link, user);
        }

        var completion = await CompleteRedemptionAsync(link, user, request, response, new List<string> { "link" });
        await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.LinkUserReturned, request, link.RedemptionCount, grant: false);
        return completion;
    }

    private async Task<IActionResult> HandlePreExistingAsync(
        SignupLink link,
        User user,
        HttpRequest request)
    {
        var orgId = link.OrganizationId ?? string.Empty;
        var alreadyMember = OrganizationAccessResolver.HasOrganizationAccess(user, orgId);

        if (alreadyMember)
        {
            var updated = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, createdUserId: string.Empty, DateTime.UtcNow);
            var ordinal = updated?.RedemptionCount ?? link.RedemptionCount + 1;
            await RecordSuccessAsync(link, user.ItemId, SignupLinkRedemptionOutcome.ExistingUserRedirected, request, ordinal, grant: false);
            return new OkObjectResult(new RedeemSignupLinkResponse
            {
                LoginUrl = BuildLoginUrl(link)
            });
        }

        // OrganizationJoined — grant in link org only; leave other orgs untouched.
        GrantOrganization(user, orgId, link.Roles, link.Permissions);
        user.LastUpdatedDate = DateTime.UtcNow;
        user.LastUpdatedBy = user.ItemId;
        await _userRepository.UpdateUserAsync(user);

        var consumed = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, createdUserId: string.Empty, DateTime.UtcNow);
        if (consumed == null)
        {
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1, userId: user.ItemId);
            return InvalidLink();
        }

        await RecordSuccessAsync(
            link,
            user.ItemId,
            SignupLinkRedemptionOutcome.OrganizationJoined,
            request,
            consumed.RedemptionCount,
            grant: true);

        return new OkObjectResult(new RedeemSignupLinkResponse
        {
            LoginUrl = BuildLoginUrl(link)
        });
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
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
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
            await RecordRejectionAsync(link, RejectionExhausted, request, ordinal: link.RedemptionCount + 1);
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
            return InvalidLink();
        }

        return await CompleteRedemptionAsync(link, completionUser, request, response, new List<string> { "link" });
    }

    private async Task<IActionResult> StartMfaChallengeAsync(SignupLink link, User user)
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
            RedirectUri = link.RedirectUri
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
            return await _embeddedTokens.IssueAsync(link, user, amr, request);
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

    private string BuildLoginUrl(SignupLink link)
    {
        var baseUrl = IamHelper.GetConfiguredIamBaseUrl(_configuration).TrimEnd('/');
        var query = new Dictionary<string, string>
        {
            ["client_id"] = link.ClientId,
            ["redirect_uri"] = link.RedirectUri,
            ["tenant_id"] = link.TenantId,
            ["login_hint"] = link.Email,
            ["organization_id"] = link.OrganizationId ?? string.Empty,
            ["response_type"] = "code",
            ["scope"] = IdpConstants.OpenIdProfileEmailScope + " offline_access"
        };

        var qs = string.Join("&", query
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"{baseUrl}/oidc/login?{qs}";
    }

    private static string? ClassifyExhaustion(SignupLink link)
    {
        if (link.Status is SignupLinkStatus.Redeemed or SignupLinkStatus.Exhausted
            || link.RedemptionCount >= link.MaxRedemptions)
        {
            return RejectionExhausted;
        }

        if (link.Status != SignupLinkStatus.Active)
        {
            return RejectionNotFound;
        }

        return null;
    }

    private static bool IsUnusableAccount(User? user, bool allowPendingVerification)
    {
        if (user == null)
        {
            return false;
        }

        if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
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

        if (allowPendingVerification && user.Status == UserLifecycleStatus.PendingVerification)
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
        string? userId = null)
    {
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
    }
}
