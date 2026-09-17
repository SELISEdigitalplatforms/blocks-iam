using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Utilities;
using Iam.DomainService.Resources;
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
        private readonly IIdpSessionService _sessionService;
        private readonly IPkceService _pkceService;
        private readonly IUserRepository _userRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationService _authenticationService;
        private readonly ITenants _tenants;
        private readonly ICacheClient _cacheClient;
        private readonly IResourceRepository _resourceRepository;
        private readonly ILogger<OidcAuthorizationEndpoint> _logger;

        public OidcAuthorizationEndpoint(
            IAuthorizationCodeRepository authCodeRepo,
            IIdpSessionRepository sessionRepo,
            IIdpSessionService sessionService,
            IPkceService pkceService,
            IUserRepository userRepository,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationService authenticationService,
            ITenants tenants,
            ICacheClient cacheClient,
            IResourceRepository resourceRepository,
            ILogger<OidcAuthorizationEndpoint> logger)
        {
            _authCodeRepo = authCodeRepo;
            _sessionRepo = sessionRepo;
            _sessionService = sessionService;
            _pkceService = pkceService;
            _userRepository = userRepository;
            _authenticationRepository = authenticationRepository;
            _authenticationService = authenticationService;
            _tenants = tenants;
            _cacheClient = cacheClient;
            _resourceRepository = resourceRepository;
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

            // Every refusal below reaches a browser, not a fetch: the relying party redirects here
            // to start the flow, and the consent screen navigates here on Allow. A body would
            // render as raw JSON in the address bar, so send the browser to the login page with
            // the error attached -- the SPA raises its blocking dialog over the card, whose single
            // action hands the user back to the application. The target is same-origin, which is
            // what makes it usable for the invalid_client and unregistered-redirect_uri refusals
            // that RFC 6749 section 4.1.2.1 forbids bouncing to the client. API callers
            // (returnRedirectResponse: false) keep the body they already parse.
            IActionResult BuildBrowserError(string error, string errorDescription)
            {
                if (!returnRedirectResponse)
                {
                    return new BadRequestObjectResult(new { error, error_description = errorDescription });
                }

                return new RedirectResult(OidcRedirectUrlBuilder.BuildLoginErrorUrl(
                    client_id,
                    response_type,
                    redirect_uri,
                    scope,
                    state,
                    nonce,
                    code_challenge,
                    code_challenge_method,
                    tenant_id,
                    error,
                    errorDescription));
            }

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

                // A tentative lookup, only to learn whether this is a device-flow client before
                // validating — deliberately NOT short-circuited on a missing/unknown client here,
                // so request-shape validation errors keep taking precedence over "unknown client"
                // exactly as before. The authoritative lookup (with its own null-check) still
                // happens below, at its original place in the flow. Skipped entirely when
                // client_id itself is blank, so invalid input still fails validation without
                // touching any dependency, same as before this lookup existed.
                var isDeviceFlowClient = !string.IsNullOrWhiteSpace(client_id)
                    && ((await _authenticationRepository.GetOidcClientRegistrationAsync(client_id))?.IsDeviceFlowClient ?? false);
                var validationResult = OidcAuthRequestValidator.Validate(authorizeRequest, isDeviceFlowClient);

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

                    return BuildBrowserError("invalid_request", string.Join("; ", validationResult.Errors));
                }

                scope = EnsureOfflineAccess(scope);

                var effectiveSessionId = request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenant_id)];

                string? resolvedUserId = blocksUserId;

                // Set only when the login orchestrator has just verified a username and password.
                // That verdict is authoritative: /oidc/login carries no way to ask for a second
                // account -- that is /oidc/session/account/add, a separate endpoint with its own
                // request model -- so a credentialed login can only ever mean "sign in as this
                // user", whatever the browser is still carrying.
                var credentialsJustVerified = !string.IsNullOrWhiteSpace(blocksUserId);

                // prompt=login demands fresh authentication, so an existing session must not sign
                // the user in silently. The credentialed path is exempt because it has already
                // performed the re-authentication prompt=login asks for, and that call passes no
                // prompt anyway, so this cannot loop.
                var forceLogin = !credentialsJustVerified && HasPromptValue(prompt, "login");

                // Silent SSO only. The session says who used this browser before, which is exactly
                // what a freshly verified password overrules. Resolving from the cookie here used to
                // overwrite the authenticated user whenever the session held exactly one account for
                // the tenant, handing the new user an authorization code minted for the previous one.
                if (!credentialsJustVerified && !forceLogin && !string.IsNullOrWhiteSpace(effectiveSessionId))
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
                    return BuildBrowserError("account_locked", "Account is temporarily locked due to failed authentication attempts");
                }

                var idpSessionId = await EnsureIdpSessionAsync(request, response, effectiveSessionId, resolvedUserId, tenant_id, credentialsJustVerified);

                var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
                if (client == null)
                {
                    _logger.LogWarning("Unknown client: {ClientId}", client_id);
                    return BuildBrowserError("invalid_client", "The application that sent you here is not recognised.");
                }

                if (client.IsDeviceFlowClient)
                {
                    // Device flow (RFC 8628): this call's only job was to authenticate the user
                    // and establish the IdP session above — there is no redirect_uri to validate
                    // and no authorization code to mint. The SPA already knows to return to the
                    // device verification page (returnUrl) on success.
                    _logger.LogInformation("Device-flow login completed for user {UserId}, client {ClientId}", resolvedUserId, client_id);
                    return new OkObjectResult(new { success = true });
                }

                if (!client.RedirectUris.Contains(redirect_uri))
                {
                    _logger.LogWarning("Invalid redirect_uri for {ClientId}: {RedirectUri}", client_id, redirect_uri);
                    return BuildBrowserError("invalid_request", "Invalid redirect_uri");
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

                // Authoritative on this path: the value is stored on the authorization code and
                // becomes the claim at exchange time without being re-validated, so the tenant's
                // real multi-organization mode has to be read here.
                var tenantConfiguration = await _resourceRepository.GetTenantConfigurationAsync();
                var effectiveOrganizationId = OrganizationAccessResolver.ResolveEffectiveOrganizationId(
                    user,
                    tenantConfiguration?.IsMultiOrgEnabled ?? false);
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
                    IdpSessionId = idpSessionId,
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

                if (returnRedirectResponse)
                {
                    return BuildBrowserError("server_error", "Internal server error");
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

        private static string EnsureOfflineAccess(string scope)
        {
            var scopes = (scope ?? string.Empty)
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!scopes.Any(s => string.Equals(s, "offline_access", StringComparison.OrdinalIgnoreCase)))
            {
                scopes.Add("offline_access");
            }

            return string.Join(' ', scopes);
        }

        private async Task<string> EnsureIdpSessionAsync(HttpRequest request, HttpResponse response, string? currentSessionId, string userId, string? tenantId, bool credentialsJustVerified)
        {
            var session = string.IsNullOrWhiteSpace(currentSessionId)
                ? null
                : await _sessionRepo.GetBySessionIdAsync(currentSessionId);

            // Someone authenticated with a password at a browser whose session belongs to a
            // different user. That is a new person at a shared browser, not a second account, so the
            // old session is torn down rather than inherited or extended. RevokeSessionAsync
            // cascades to the refresh tokens bound to the session, so the previous user cannot be
            // resumed in another tab of this browser; sessions are per-browser, so their other
            // devices are untouched.
            if (credentialsJustVerified
                && session != null
                && !session.RevokedAt.HasValue
                && !session.IsExpired()
                && SessionHoldsAnotherUser(session, userId, tenantId))
            {
                _logger.LogInformation(
                    "Revoking IdP session {SessionId}: re-authenticated as a different user for tenant {TenantId}",
                    session.SessionId,
                    tenantId);

                await _sessionService.RevokeSessionAsync(session.SessionId, "reauthenticated_as_different_user");
                session = null;
            }

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
                return newSession.SessionId;
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

            var resolvedSessionId = session.SessionId;

            // Session fixation defence: an id that was already in the browser must not survive an
            // authentication event. account/add and account/select have always rotated; login did
            // not, so a planted idp_session_id cookie outlived the login it was planted for.
            // Rotation carries the accounts over and re-points the refresh tokens, and returns null
            // if the row went away underneath us -- in which case the existing id still stands.
            if (credentialsJustVerified)
            {
                var rotatedSessionId = await _sessionService.RotateSessionAsync(session.SessionId, "password_login");
                if (!string.IsNullOrWhiteSpace(rotatedSessionId))
                {
                    resolvedSessionId = rotatedSessionId;
                }
            }

            SetIdpSessionCookie(request, response, tenantId, resolvedSessionId, session.AbsoluteExpiry);
            return resolvedSessionId;
        }

        /// <summary>
        /// Whether the session speaks for someone other than <paramref name="userId"/>.
        ///
        /// Scoped to the tenant slot the login is for, matching how the account-exists check above
        /// compares accounts. Accounts under other tenants are left alone deliberately: one session
        /// legitimately holds a user per tenant under multi-account SSO, and signing in to one
        /// tenant must not sign the person out of the others.
        /// </summary>
        private static bool SessionHoldsAnotherUser(IdpSessionModel session, string userId, string? tenantId)
        {
            return session.Accounts.Any(a =>
                string.Equals(a.TenantId ?? string.Empty, tenantId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasPromptValue(string? prompt, string value) =>
            !string.IsNullOrWhiteSpace(prompt)
            && prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));

        private void SetIdpSessionCookie(
            HttpRequest httpRequest,
            HttpResponse response,
            string? tenantId,
            string sessionId,
            DateTime absoluteExpiry)
        {
            var effectiveExpiry = absoluteExpiry == default
                ? DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                : absoluteExpiry;

            var tenant = string.IsNullOrWhiteSpace(tenantId) ? null : _tenants.GetTenantByID(tenantId);
            IdpSessionCookie.Append(httpRequest, response, tenant, tenantId, sessionId, effectiveExpiry);
        }

        private async Task PersistLastUsedOrganizationAsync(User user, string? organizationId)
        {
            // "default" and "no-org" are scope sentinels, not organizations, so there is nothing to
            // remember: persisting one would leave the field claiming a membership the user does not
            // have. Revoking the last membership sets "no-org" here deliberately; a sign-in must not.
            if (string.IsNullOrWhiteSpace(organizationId)
                || OrganizationScopeResolver.IsReservedOrganizationId(organizationId)
                || string.Equals(user.LastUsedOrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                user.LastUsedOrganizationId = organizationId;

                // One field, by $set. This used to be a whole-document replace of a snapshot read
                // earlier in the request, which wrote back every other field as it looked at read
                // time -- reverting anything a concurrent request had changed in between, the login
                // counter included. Nothing here needs to write more than the one field it names.
                await _authenticationRepository.UpdatePartialAsync<User>(
                    user.ItemId,
                    new Dictionary<string, object>
                    {
                        { nameof(User.LastUsedOrganizationId), organizationId }
                    });
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
