using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Implements the OIDC authorization endpoint: validates the request, enforces the active IdP
    /// session, mints an <see cref="AuthorizationCodeModel"/>, and builds the redirect (or JSON) response.
    /// Extracted from <c>AuthorizationFlowService.AuthorizeAsync</c> so the entry orchestrator stays thin.
    /// </summary>
    public sealed class OidcAuthorizationEndpoint
    {
        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IIdpSessionRepository _sessionRepo;
        private readonly IPkceService _pkceService;
        private readonly IUserRepository _userRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationService _authenticationService;
        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<OidcAuthorizationEndpoint> _logger;

        public OidcAuthorizationEndpoint(
            IAuthorizationCodeRepository authCodeRepo,
            IIdpSessionRepository sessionRepo,
            IPkceService pkceService,
            IUserRepository userRepository,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationService authenticationService,
            ITenants tenants,
            ICacheClient cacheClient,
            ILogger<OidcAuthorizationEndpoint> logger)
        {
            _authCodeRepo = authCodeRepo;
            _sessionRepo = sessionRepo;
            _pkceService = pkceService;
            _userRepository = userRepository;
            _authenticationRepository = authenticationRepository;
            _authenticationService = authenticationService;
            _tenants = tenants;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public async Task<IActionResult> AuthorizeAsync(
            string client_id,
            string response_type,
            string redirect_uri,
            string scope,
            string state,
            string nonce,
            string code_challenge,
            string code_challenge_method,
            string? prompt,
            string? tenant_id,
            HttpRequest request,
            HttpResponse response,
            string? blocksUserId = null,
            bool returnRedirectResponse = true,
            bool mfaCompleted = false)
        {
            var canRedirectToClient = false;

            try
            {
                var authorizeRequest = new AuthorizeRequest
                {
                    ClientId = client_id,
                    ResponseType = response_type,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    State = state,
                    Nonce = nonce,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    Prompt = prompt
                };

                var validationResult = OidcAuthRequestValidator.Validate(authorizeRequest);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Authorization request validation failed for {ClientId}: {Errors}", client_id, string.Join(", ", validationResult.Errors));

                    var errorParams = new Dictionary<string, string>
                    {
                        { "error", "invalid_request" },
                        { "error_description", string.Join("; ", validationResult.Errors) },
                        { "state", state }
                    };

                    if (returnRedirectResponse && !string.IsNullOrWhiteSpace(redirect_uri))
                    {
                        return new RedirectResult(OidcRedirectUrlBuilder.BuildRedirectUri(redirect_uri, errorParams));
                    }

                    return new BadRequestObjectResult(new
                    {
                        error = "invalid_request",
                        error_description = string.Join("; ", validationResult.Errors)
                    });
                }

                var effectiveSessionId = request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenant_id)];

                string? resolvedUserId = blocksUserId;

                if (!string.IsNullOrWhiteSpace(effectiveSessionId))
                {
                    var session = await _sessionRepo.GetBySessionIdAsync(effectiveSessionId);
                    if (session != null && !session.RevokedAt.HasValue && !session.IsExpired())
                    {
                        var sessionAccounts = session.Accounts.AsEnumerable();
                        if (!string.IsNullOrWhiteSpace(tenant_id))
                        {
                            sessionAccounts = sessionAccounts.Where(a => string.Equals(a.TenantId, tenant_id, StringComparison.OrdinalIgnoreCase));
                        }

                        var filteredAccounts = sessionAccounts.ToList();

                        if (filteredAccounts.Count == 1)
                        {
                            resolvedUserId = filteredAccounts[0].UserId;
                            await _sessionRepo.UpdateActivityAsync(effectiveSessionId);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    _logger.LogInformation("Unauthenticated authorization request for {ClientId}", client_id);
                    return new RedirectResult(OidcRedirectUrlBuilder.BuildLoginUrl(client_id, response_type, redirect_uri, scope, state, nonce, code_challenge, code_challenge_method, tenant_id));
                }

                var lockoutCheckUser = await _userRepository.GetUserByIdAsync(resolvedUserId);
                if (lockoutCheckUser != null
                    && lockoutCheckUser.LockoutUntilUtc.HasValue
                    && lockoutCheckUser.LockoutUntilUtc.Value > DateTime.UtcNow)
                {
                    _logger.LogWarning("Authorize request denied for locked account {UserId}", resolvedUserId);
                    return new BadRequestObjectResult(new
                    {
                        error = "account_locked",
                        error_description = "Account is temporarily locked due to failed authentication attempts"
                    });
                }

                await EnsureIdpSessionAsync(request, response, effectiveSessionId, resolvedUserId, tenant_id);

                var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
                if (client == null)
                {
                    _logger.LogWarning("Unknown client: {ClientId}", client_id);
                    return new BadRequestObjectResult(new { error = "invalid_client" });
                }

                if (!client.RedirectUris.Contains(redirect_uri))
                {
                    _logger.LogWarning("Invalid redirect_uri for {ClientId}: {RedirectUri}", client_id, redirect_uri);
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Invalid redirect_uri" });
                }

                var cacheKey = $"idp_flow:{state}";
                var flowContextJson = await _cacheClient.GetStringValueAsync(cacheKey);
                var forwardedToContext = flowContextJson != null ? JsonSerializer.Deserialize<FlowContext>(flowContextJson) : null;

                canRedirectToClient = true;

                IActionResult BuildAuthorizeError(string error, string errorDescription)
                {
                    if (returnRedirectResponse && canRedirectToClient)
                    {
                        var errorParams = new Dictionary<string, string>
                        {
                            { "error", error },
                            { "error_description", errorDescription },
                            { "state", state },
                            { "forwardedTo", forwardedToContext?.ForwardedTo ?? string.Empty },
                        };

                        return new RedirectResult(OidcRedirectUrlBuilder.BuildRedirectUri(redirect_uri, errorParams));
                    }

                    return new BadRequestObjectResult(new
                    {
                        error,
                        error_description = errorDescription
                    });
                }

                var user = await _userRepository.GetUserByIdAsync(resolvedUserId);
                if (user == null)
                {
                    return BuildAuthorizeError("access_denied", "User not found");
                }

                if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
                {
                    return BuildAuthorizeError("account_locked", "Account is temporarily locked due to failed authentication attempts");
                }

                var effectiveOrganizationId = OrganizationAccessResolver.ResolveEffectiveOrganizationId(user);
                await PersistLastUsedOrganizationAsync(user, effectiveOrganizationId);

                var authCode = _pkceService.GenerateRandomCode(32);
                var amr = BuildAmr(user, mfaCompleted);

                var codeModel = new AuthorizationCodeModel
                {
                    Code = authCode,
                    ClientId = client_id,
                    TenantId = tenant_id,
                    UserId = resolvedUserId,
                    OrganizationId = effectiveOrganizationId,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    Nonce = nonce,
                    State = state,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    Amr = amr,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(request),
                };

                // Blocks Cloud Impersonation Support
                var userPrincipal = await _authenticationService.GetPrincipalFromTokenAsync(request, BlocksContext.GetContext()?.TenantId ?? "", IsUserInfoGetRequest: false);

                if (userPrincipal != null)
                {
                    bool.TryParse(userPrincipal?.FindFirst("impersonated")?.Value, out bool impersonated);

                    var claimUserId = string.IsNullOrWhiteSpace(userPrincipal?.FindFirst("sub")?.Value) ?
                                        userPrincipal?.FindFirst("user_id")?.Value :
                                        userPrincipal?.FindFirst("sub")?.Value;

                    var claimTenantId = userPrincipal?.FindFirst("tenant_id")?.Value;

                    codeModel.Impersonated = impersonated;
                    codeModel.ImpersonatedUserId = claimUserId;
                    codeModel.TargetedTenantId = claimTenantId;
                }

                _logger.LogDebug("Authorization code model: {CodeModel}", codeModel);

                await _authCodeRepo.CreateAsync(codeModel);

                _logger.LogInformation("Authorization code issued for user {UserId}, client {ClientId}", resolvedUserId, client_id);

                var callbackParams = new Dictionary<string, string>
                {
                    { "code", authCode },
                    { "state", state },
                    { "tenant_id", tenant_id ?? string.Empty },
                    { "forwardedTo", forwardedToContext?.ForwardedTo ?? string.Empty }
                };

                var callbackUri = OidcRedirectUrlBuilder.BuildRedirectUri(redirect_uri, callbackParams);

                if (returnRedirectResponse)
                {
                    return new RedirectResult(callbackUri);
                }

                return new OkObjectResult(new
                {
                    redirect_uri = callbackUri
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in authorization endpoint");

                if (returnRedirectResponse && canRedirectToClient)
                {
                    var errorParams = new Dictionary<string, string>
                    {
                        { "error", "server_error" },
                        { "error_description", "Internal server error" },
                        { "state", state }
                    };

                    return new RedirectResult(OidcRedirectUrlBuilder.BuildRedirectUri(redirect_uri, errorParams));
                }

                return new ObjectResult(new { error = "server_error", error_description = "Internal server error" })
                {
                    StatusCode = 500
                };
            }
        }

        private static List<string> BuildAmr(User user, bool mfaCompleted)
        {
            var amr = new List<string> { "pwd" };
            if (mfaCompleted)
            {
                amr.Add(user.UserMfaType == UserMfaType.TOTP ? "totp" : "otp");
            }

            return amr;
        }

        private async Task EnsureIdpSessionAsync(HttpRequest request, HttpResponse response, string? currentSessionId, string userId, string? tenantId)
        {
            var session = string.IsNullOrWhiteSpace(currentSessionId)
                ? null
                : await _sessionRepo.GetBySessionIdAsync(currentSessionId);

            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                var newSession = new IdpSessionModel
                {
                    SessionId = Guid.NewGuid().ToString("n"),
                    TenantId = tenantId,
                    Accounts =
                    [
                        new IdpSessionAccount
                        {
                            UserId = userId,
                            TenantId = tenantId,
                            DisplayName = userId,
                            LoginAt = DateTime.UtcNow
                        }
                    ],
                    IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(request),
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    IdleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout()),
                    AbsoluteExpiry = DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                };

                await _sessionRepo.CreateAsync(newSession);
                SetIdpSessionCookie(request, response, tenantId, newSession.SessionId, newSession.AbsoluteExpiry);
                return;
            }

            var accountExists = session.Accounts.Any(a =>
                string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

            if (!accountExists)
            {
                await _sessionRepo.AddAccountAsync(session.SessionId, new IdpSessionAccount
                {
                    UserId = userId,
                    TenantId = tenantId,
                    DisplayName = userId,
                    LoginAt = DateTime.UtcNow
                });
            }
            else
            {
                await _sessionRepo.UpdateActivityAsync(session.SessionId);
            }

            SetIdpSessionCookie(request, response, tenantId, session.SessionId, session.AbsoluteExpiry);
        }

        private void SetIdpSessionCookie(
            HttpRequest httpRequest,
            HttpResponse response,
            string? tenantId,
            string sessionId,
            DateTime absoluteExpiry)
        {
            var isLocal = DomainResolver.IsLocalhost();
            var domain = BlocksContext.ResolveApplicationDomain(httpRequest);
            var effectiveExpiry = absoluteExpiry == default
                ? DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                : absoluteExpiry;

            string? resolvedDomain = null;
            if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(tenantId))
            {
                var tenant = _tenants.GetTenantByID(tenantId);
                resolvedDomain = tenant.IsRootTenant
                    ? DomainResolver.GetRootDomain(domain)
                    : domain;
            }

            var cookieOptions = isLocal
                ? DomainResolver.CreateLoopbackCookieOptions(resolvedDomain, effectiveExpiry)
                : DomainResolver.CreateProductionCookieOptions(resolvedDomain, effectiveExpiry);

            response.Cookies.Append(
                IdpConstants.BuildIdpSessionCookieKey(tenantId),
                sessionId,
                cookieOptions);
        }

        private async Task PersistLastUsedOrganizationAsync(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId)
                || string.Equals(user.LastUsedOrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                user.LastUsedOrganizationId = organizationId;
                await _userRepository.UpdateUserAsync(user);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist last used organization for user {UserId}", user.ItemId);
            }
        }

        private static TimeSpan GetIdpSessionIdleTimeout()
        {
            return SessionTimeoutConfig.GetIdleTimeout();
        }

        private static TimeSpan GetIdpSessionAbsoluteTimeout()
        {
            return SessionTimeoutConfig.GetAbsoluteTimeoutHours();
        }

        private sealed class FlowContext
        {
            [JsonPropertyName("forwardedTo")]
            public string? ForwardedTo { get; set; } = null!;
        }
    }
}
