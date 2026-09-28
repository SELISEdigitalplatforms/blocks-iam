using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
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

namespace Authentication.DomainService.SignupLinks;

/// <summary>
/// Phase 3 Passwordless redemption: create user, restricted link session, authorizeUrl.
/// Returning-user / PasswordRequired / MFA branches are Phase 4.
/// </summary>
public sealed class SignupLinkRedemptionOrchestrator : ISignupLinkRedemptionOrchestrator
{
    private readonly ISignupLinkRepository _links;
    private readonly ISignupLinkRedemptionRepository _redemptions;
    private readonly ILinkSessionRepository _linkSessions;
    private readonly IOidcClientRegistrationLookup _oidcLookup;
    private readonly IUserRepository _userRepository;
    private readonly IUserManagementMutationService _userMutation;
    private readonly ICacheClient _cacheClient;
    private readonly ITenants _tenants;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SignupLinkRedemptionOrchestrator> _logger;

    public SignupLinkRedemptionOrchestrator(
        ISignupLinkRepository links,
        ISignupLinkRedemptionRepository redemptions,
        ILinkSessionRepository linkSessions,
        IOidcClientRegistrationLookup oidcLookup,
        IUserRepository userRepository,
        IUserManagementMutationService userMutation,
        ICacheClient cacheClient,
        ITenants tenants,
        IConfiguration configuration,
        ILogger<SignupLinkRedemptionOrchestrator> logger)
    {
        _links = links;
        _redemptions = redemptions;
        _linkSessions = linkSessions;
        _oidcLookup = oidcLookup;
        _userRepository = userRepository;
        _userMutation = userMutation;
        _cacheClient = cacheClient;
        _tenants = tenants;
        _configuration = configuration;
        _logger = logger;
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

        // Never log the code (C5).
        var hash = SignupLinkCodeHasher.Hash(code);
        var link = await _links.GetByCodeHashAsync(hash);
        if (link == null)
        {
            return InvalidLink();
        }

        if (!string.IsNullOrWhiteSpace(tenantIdHint)
            && !string.Equals(link.TenantId, tenantIdHint, StringComparison.OrdinalIgnoreCase))
        {
            await RecordRejectionAsync(link, "not_found", request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        var rejection = await ClassifyRejectionAsync(link);
        if (rejection != null)
        {
            await RecordRejectionAsync(link, rejection, request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        // Phase 3: Passwordless only. PasswordRequired is Phase 4.
        if (link.CredentialMode != SignupLinkCredentialMode.Passwordless)
        {
            await RecordRejectionAsync(link, "not_found", request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        var existing = await _userRepository.GetUserByEmailAsync(link.Email);
        if (existing != null)
        {
            // Returning-user branches are Phase 4.
            await RecordRejectionAsync(link, "not_found", request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        var clientOk = await _oidcLookup.GetByClientIdAsync(link.ClientId);
        if (clientOk == null || !clientOk.IsActive
            || !clientOk.RedirectUris.Any(u => string.Equals(u, link.RedirectUri, StringComparison.Ordinal)))
        {
            await RecordRejectionAsync(link, "client_invalid", request, ordinal: link.RedemptionCount + 1);
            return InvalidLink();
        }

        var userId = await CreateSignupLinkUserAsync(link);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError("Signup link redeem failed to create user for link {LinkId}", link.ItemId);
            return InvalidLink();
        }

        var now = DateTime.UtcNow;
        var updated = await _links.TryIncrementRedemptionAsync(link.ItemId, link.TenantId, userId, now);
        if (updated == null)
        {
            // Lost the race (C3) — do not leave an orphan account preferred; still refuse.
            await RecordRejectionAsync(link, "exhausted", request, ordinal: link.RedemptionCount + 1, userId: null);
            return InvalidLink();
        }

        await _redemptions.InsertAsync(new SignupLinkRedemption
        {
            ItemId = Guid.NewGuid().ToString("n"),
            LinkId = link.ItemId,
            TenantId = link.TenantId,
            Email = link.Email,
            UserId = userId,
            Outcome = SignupLinkRedemptionOutcome.UserCreated,
            RedemptionOrdinal = updated.RedemptionCount,
            RedeemedAtUtc = now,
            IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(request),
            UserAgent = request.Headers.UserAgent.ToString(),
            GrantedOrganizationId = link.OrganizationId,
            GrantedRoles = link.Roles?.ToList() ?? [],
            GrantedPermissions = link.Permissions?.ToList() ?? [],
            CreatedDate = now,
            CreatedBy = userId,
            LastUpdatedDate = now,
            LastUpdatedBy = userId
        });

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
            IssuedAtUtc = now,
            ExpiresAtUtc = expires
        };
        await _linkSessions.CreateAsync(session);

        var tenant = _tenants.GetTenantByID(link.TenantId);
        LinkSessionCookie.Append(request, response, tenant, link.TenantId, session.SessionId, expires);

        var authorizeUrl = await BuildAuthorizeUrlAsync(link);
        return new OkObjectResult(new RedeemSignupLinkResponse { AuthorizeUrl = authorizeUrl });
    }

    private async Task<string?> ClassifyRejectionAsync(SignupLink link)
    {
        if (link.Status == SignupLinkStatus.Revoked)
        {
            return "revoked";
        }

        if (link.ExpiresAtUtc <= DateTime.UtcNow || link.Status == SignupLinkStatus.Expired)
        {
            return "expired";
        }

        if (link.Status is SignupLinkStatus.Redeemed or SignupLinkStatus.Exhausted
            || link.RedemptionCount >= link.MaxRedemptions)
        {
            return "exhausted";
        }

        if (link.Status != SignupLinkStatus.Active)
        {
            return "not_found";
        }

        await Task.CompletedTask;
        return null;
    }

    private async Task<string> CreateSignupLinkUserAsync(SignupLink link)
    {
        // Establish BlocksContext tenant for MapUser / CreateUser.
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
                UserPassType = UserPassType.None,
                UserCreationType = UserCreationType.Api,
                VerifiedType = UserVerifiedType.Email,
                AllowedLogInType = new List<UserLogInType> { UserLogInType.AuthrizationCode },
                MfaEnabled = false,
                UserMfaType = UserMfaType.None
            };

            var user = _userMutation.MapUser(request);
            user.Active = true;
            user.IsVerified = true;
            user.Status = UserLifecycleStatus.Active;
            user.EmailVerifiedAtUtc = DateTime.UtcNow;
            user.ProvisioningSource = UserProvisioningSource.API;
            user.AllowedLogInType = new List<UserLogInType> { UserLogInType.AuthrizationCode };
            user.UserPassType = UserPassType.None;
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

    private async Task<string> BuildAuthorizeUrlAsync(SignupLink link)
    {
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(16));

        var flowContext = new
        {
            state,
            nonce,
            codeVerifier = (string?)null,
            provider = "blocks",
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

        var qs = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"{baseUrl}/api/oidc/authorize?{qs}";
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
}
