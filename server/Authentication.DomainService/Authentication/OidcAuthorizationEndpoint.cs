using Authentication.DomainService.Utilities;
using Authentication.DomainService.Authentication.LoginPolicies;
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
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Implements the OIDC authorization endpoint: validates the request, enforces the active IdP
    /// session, mints an <see cref="AuthorizationCodeModel"/>, and builds the redirect (or JSON) response.
    /// Extracted from <c>AuthorizationFlowService.AuthorizeAsync</c> so the entry orchestrator stays thin.
    /// </summary>
    public sealed class OidcAuthorizationEndpoint
    {
        private const string ErrorInvalidRequest = "invalid_request";
        private const string ErrorInvalidClient = "invalid_client";
        private const string UnknownClientMessage = "The application that sent you here is not recognised.";

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
        private readonly ILinkSessionRepository _linkSessionRepo;
        private readonly ILoginPolicyService _loginPolicy;
        private readonly ILogger<OidcAuthorizationEndpoint> _logger;

        public OidcAuthorizationEndpoint(
            OidcAuthorizationSessionStores sessionStores,
            OidcAuthorizationIdentityStores identityStores,
            ILogger<OidcAuthorizationEndpoint> logger)
        {
            _authCodeRepo = sessionStores.AuthCodes;
            _sessionRepo = sessionStores.Sessions;
            _sessionService = sessionStores.SessionService;
            _pkceService = sessionStores.Pkce;
            _linkSessionRepo = sessionStores.LinkSessions;
            _logger = logger;
            _userRepository = identityStores.Users;
            _authenticationRepository = identityStores.Authentication;
            _authenticationService = identityStores.AuthenticationService;
            _tenants = identityStores.Tenants;
            _cacheClient = identityStores.Cache;
            _resourceRepository = identityStores.Resources;
            _loginPolicy = identityStores.LoginPolicy;
        }

        public async Task<IActionResult> AuthorizeAsync( // NOSONAR S3776 — OIDC authorize branches are intentional; tracked separately from signup-links
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
            bool mfaCompleted = false,
            string? loginMethod = null)
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
                    new OidcLoginUrlRequest
                    {
                        ClientId = client_id,
                        ResponseType = response_type,
                        RedirectUri = redirect_uri,
                        Scope = scope,
                        State = state,
                        Nonce = nonce,
                        CodeChallenge = code_challenge,
                        CodeChallengeMethod = code_challenge_method,
                        TenantId = tenant_id
                    },
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
                var earlyClient = !string.IsNullOrWhiteSpace(client_id)
                    ? await _authenticationRepository.GetOidcClientRegistrationAsync(client_id)
                    : null;
                var isDeviceFlowClient = earlyClient?.IsDeviceFlowClient ?? false;
                var validationResult = OidcAuthRequestValidator.Validate(authorizeRequest, isDeviceFlowClient);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Authorization request validation failed for {ClientId}: {Errors}", client_id, string.Join(", ", validationResult.Errors));

                    // Never bounce to redirect_uri until it has been matched to a registered
                    // client (RFC 6749 §4.1.2.1 / CWE-601). An unvalidated URI here is exactly
                    // how an attacker-controlled Location is produced. Same-origin login error
                    // page (or 400 for API callers) instead.
                    return BuildBrowserError(ErrorInvalidRequest, string.Join("; ", validationResult.Errors));
                }

                // An inactive client is refused like an unknown one, before the login hop or any session work.
                if (earlyClient != null && !earlyClient.IsActive)
                {
                    _logger.LogWarning("Inactive client refused: {ClientId}", client_id);
                    return BuildBrowserError(ErrorInvalidClient, UnknownClientMessage);
                }

                // Tenant login policy: the IAM login page must be on, and a just-verified sign-in must
                // use a method this client allows. Silent sign-in is filtered further below.
                var policyDecision = await _loginPolicy.CheckChannelAsync(LoginChannel.Oidc);
                if (policyDecision.IsAllowed && !string.IsNullOrWhiteSpace(blocksUserId) && !string.IsNullOrWhiteSpace(loginMethod))
                {
                    policyDecision = await _loginPolicy.CheckAsync(LoginChannel.Oidc, loginMethod, earlyClient);
                }

                if (!policyDecision.IsAllowed)
                {
                    _logger.LogInformation("Sign-in refused by the login policy for client {ClientId}", client_id);
                    return BuildBrowserError(LoginPolicyRules.Error, policyDecision.Message!);
                }

                // Reject unregistered redirect_uri before the login hop or any later client
                // callback. Unknown clients are still handled later (invalid_client) so the
                // unauthenticated "please sign in" path is unchanged when client_id is typo'd.
                if (earlyClient != null
                    && !earlyClient.IsDeviceFlowClient
                    && !earlyClient.RedirectUris.Contains(redirect_uri))
                {
                    _logger.LogWarning("Invalid redirect_uri rejected for OIDC client registration check");
                    return BuildBrowserError(ErrorInvalidRequest, "Invalid redirect_uri");
                }

                // RequirePkce is stored per client (on by default) and was never read, so a login could
                // start without PKCE even for a client that requires it. Refuse before the login hop.
                if (earlyClient != null
                    && !earlyClient.IsDeviceFlowClient
                    && earlyClient.RequirePkce
                    && string.IsNullOrWhiteSpace(code_challenge))
                {
                    _logger.LogWarning("Authorize without PKCE refused for client {ClientId}", client_id);
                    return BuildBrowserError(ErrorInvalidRequest, "PKCE code_challenge is required");
                }

                scope = EnsureOfflineAccess(scope);

                var effectiveSessionId = request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenant_id)];

                // Set only when the login orchestrator has just verified a username and password.
                // That verdict is authoritative: /oidc/login carries no way to ask for a second
                // account -- that is /oidc/session/account/add, a separate endpoint with its own
                // request model -- so a credentialed login can only ever mean "sign in as this
                // user", whatever the browser is still carrying.
                var credentialsJustVerified = !string.IsNullOrWhiteSpace(blocksUserId);

                string? resolvedUserId = blocksUserId;
                LinkSessionModel? matchedLinkSession = null;
                var linkCookieId = request.Cookies[IdpConstants.BuildLinkSessionCookieKey(tenant_id)];
                if (!string.IsNullOrWhiteSpace(linkCookieId))
                {
                    // A freshly verified username and password supersedes the restricted link
                    // session for exactly the reason it supersedes the IdP session cookie below:
                    // the token that follows has to describe the password login, not a signup
                    // link redeemed in this browser earlier. Without this, signing in normally
                    // inside the link session's lifetime still minted amr=["link"], the link's
                    // organization, roles intersected against the link's frozen grant, and an
                    // audience narrowed to the link's client -- on an ordinary login, for the
                    // redeeming user and for anyone else who signed in on the same browser.
                    // The cookie is cleared rather than only ignored, so the next silent
                    // authorize does not pick it up again for the rest of its lifetime.
                    // The session is ended too (it is stored per tenant, so a blank tenant_id skips the
                    // store; such a request never carries the tenant's cookie anyway).
                    if (credentialsJustVerified)
                    {
                        if (!string.IsNullOrWhiteSpace(tenant_id))
                        {
                            await _linkSessionRepo.DeleteAsync(tenant_id, linkCookieId);
                        }
                        ClearLinkSessionCookie(request, response, tenant_id);
                    }
                    else if (!string.IsNullOrWhiteSpace(tenant_id))
                    {
                        var linkSession = await _linkSessionRepo.GetBySessionIdAsync(tenant_id, linkCookieId);
                        if (linkSession != null
                            && !linkSession.IsExpired()
                            && string.Equals(linkSession.ClientId, client_id, StringComparison.Ordinal)
                            && string.Equals(linkSession.TenantId, tenant_id, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedLinkSession = linkSession;
                            if (string.IsNullOrWhiteSpace(resolvedUserId))
                            {
                                resolvedUserId = linkSession.UserId;
                            }
                        }
                        // Wrong client or expired: ignore entirely (C4) — fall through to IdP / login.
                    }
                }

                // prompt=login demands fresh authentication, so an existing session must not sign
                // the user in silently. The credentialed path is exempt because it has already
                // performed the re-authentication prompt=login asks for, and that call passes no
                // prompt anyway, so this cannot loop.
                var forceLogin = !credentialsJustVerified && HasPromptValue(prompt, "login");

                // Silent SSO only. The session says who used this browser before, which is exactly
                // what a freshly verified password overrules. Resolving from the cookie here used to
                // overwrite the authenticated user whenever the session held exactly one account for
                // the tenant, handing the new user an authorization code minted for the previous one.
                // Link-session resolution is authoritative for its matching client (H3/C4).
                // Do not let a lingering IdP cookie overwrite the freshly redeemed user.
                if (matchedLinkSession == null
                    && !credentialsJustVerified
                    && !forceLogin
                    && !string.IsNullOrWhiteSpace(effectiveSessionId))
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
                        var accountAllowed = await _loginPolicy.SessionAccountFilterAsync(earlyClient);

                        // An account that signed in with a method this client does not allow signs in again.
                        if (filteredAccounts.Count == 1 && accountAllowed(filteredAccounts[0]))
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

                var idpSessionId = await EnsureIdpSessionAsync(request, response, effectiveSessionId, resolvedUserId, tenant_id, credentialsJustVerified, loginMethod);

                var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
                if (client == null || !client.IsActive)
                {
                    _logger.LogWarning("Unknown client: {ClientId}", client_id);
                    return BuildBrowserError(ErrorInvalidClient, UnknownClientMessage);
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
                    _logger.LogWarning("Invalid redirect_uri rejected for OIDC client registration check");
                    return BuildBrowserError(ErrorInvalidRequest, "Invalid redirect_uri");
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
                var effectiveOrganizationId = matchedLinkSession != null
                    ? matchedLinkSession.OrganizationId
                    : OrganizationAccessResolver.ResolveEffectiveOrganizationId(
                        user,
                        tenantConfiguration?.IsMultiOrgEnabled ?? false);
                await PersistLastUsedOrganizationAsync(user, effectiveOrganizationId);

                var authCode = _pkceService.GenerateRandomCode(32);
                List<string> amr;
                if (matchedLinkSession != null)
                {
                    amr = matchedLinkSession.Amr is { Count: > 0 }
                        ? matchedLinkSession.Amr.ToList()
                        : new List<string> { "link" };
                }
                else
                {
                    amr = BuildAmr(user, mfaCompleted);
                }

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
                    CreatedByUserAgent = request.Headers.UserAgent.ToString(),
                    IdpSessionId = idpSessionId,
                    IsLinkAuthentication = matchedLinkSession != null,
                    RestrictedRoles = matchedLinkSession?.Roles?.ToList() ?? [],
                    RestrictedPermissions = matchedLinkSession?.Permissions?.ToList() ?? [],
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

        private async Task<string> EnsureIdpSessionAsync(HttpRequest request, HttpResponse response, string? currentSessionId, string userId, string? tenantId, bool credentialsJustVerified, string? loginMethod)
        {
            // Recorded only for a sign-in that just happened, so a silent sign-in can follow a client's method.
            var recordedLoginMethod = credentialsJustVerified ? loginMethod : null;

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
                            LoginAt = DateTime.UtcNow,
                            LoginMethod = recordedLoginMethod
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
                    LoginAt = DateTime.UtcNow,
                    LoginMethod = recordedLoginMethod
                });
            }
            else
            {
                await _sessionRepo.UpdateActivityAsync(session.SessionId);
                if (!string.IsNullOrWhiteSpace(recordedLoginMethod))
                {
                    await _sessionRepo.SetAccountLoginMethodAsync(session.SessionId, userId, tenantId ?? string.Empty, recordedLoginMethod);
                }
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

        private void ClearLinkSessionCookie(
            HttpRequest httpRequest,
            HttpResponse response,
            string? tenantId)
        {
            var tenant = string.IsNullOrWhiteSpace(tenantId) ? null : _tenants.GetTenantByID(tenantId);
            LinkSessionCookie.Clear(httpRequest, response, tenant, tenantId);
        }

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
