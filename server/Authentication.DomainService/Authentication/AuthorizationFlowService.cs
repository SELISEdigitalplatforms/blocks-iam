using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Validation;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Users;
using Iam.DomainService.Entities;
using Authentication.DomainService.Entities;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Headers;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Authentication.DomainService.Services;
using BCryptNet = BCrypt.Net.BCrypt;

namespace Authentication.DomainService.Authentication
{
    public class AuthorizationFlowService : IAuthorizationFlowService
    {
        private const string IdpSessionCookieName = "idp_session_id";
        private const string PendingSelectedUserCookieName = "idp_selected_user_id";
        private const string PendingSelectedTenantCookieName = "idp_selected_tenant_id";

        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly IIdpSessionRepository _sessionRepo;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly ITokenGenerationService _tokenService;
        private readonly IPkceService _pkceService;
        private readonly AuthorizeRequestValidator _authorizeValidator;
        private readonly IUserRepository _userRepository;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ClientCredentialAuthorizationService _clientCredentialAuthorizationService;
        private readonly ITenants _tenants;
        private readonly ILogger<AuthorizationFlowService> _logger;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICacheClient _cacheClient;

        public AuthorizationFlowService(
            IAuthorizationCodeRepository authCodeRepo,
            IRefreshTokenRepository refreshTokenRepo,
            IIdpSessionRepository sessionRepo,
            IAuditLogRepository auditLogRepo,
            ITokenGenerationService tokenService,
            IPkceService pkceService,
            AuthorizeRequestValidator authorizeValidator,
            IUserRepository userRepository,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IAuthenticationRepository authenticationRepository,
            ClientCredentialAuthorizationService clientCredentialAuthorizationService,
            ITenants tenants,
            IAuthenticationService authenticationService,
            ICacheClient cacheClient,
            ILogger<AuthorizationFlowService> logger)
        {
            _authCodeRepo = authCodeRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _sessionRepo = sessionRepo;
            _auditLogRepo = auditLogRepo;
            _tokenService = tokenService;
            _pkceService = pkceService;
            _authorizeValidator = authorizeValidator;
            _userRepository = userRepository;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _authenticationRepository = authenticationRepository;
            _clientCredentialAuthorizationService = clientCredentialAuthorizationService;
            _tenants = tenants;
            _authenticationService = authenticationService;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public async Task<IActionResult> ExecuteOidcLoginAsync(OidcLoginRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            // If provider is specified, initiate social authentication flow
            if (!string.IsNullOrWhiteSpace(request.Provider))
            {
                // Generate OIDC state to track this authentication flow through social provider
                var oidcState = Guid.NewGuid().ToString("n");

                // Store OIDC context in cache for the entire flow
                // Key: oidc_context:{oidcState}
                // Value: { clientId, state, redirectUri, ... } - the original OIDC request parameters
                var contextKey = $"oidc_context:{oidcState}";
                var contextValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    clientId = request.ClientId,
                    state = request.State,
                    redirectUri = request.RedirectUri,
                    scope = request.Scope,
                    nonce = request.Nonce,
                    codeChallenge = request.CodeChallenge,
                    codeChallengeMethod = request.CodeChallengeMethod,
                    tenantId = request.TenantId,
                    createdAt = DateTime.UtcNow
                });
                await _cacheClient.AddStringValueAsync(contextKey, contextValue, 600); // 10 minute TTL

                // Get social authorization URL
                return await _authenticationService.GetOidcSocialAuthorizationUrlAsync(request.Provider, oidcState, request.RedirectUri ?? string.Empty);
            }

            // Standard password-based OIDC login flow
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "username and password are required" });

            if (string.IsNullOrWhiteSpace(request.ClientId))
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "client_id is required" });

            if (string.IsNullOrWhiteSpace(request.RedirectUri))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirect_uri is required" });

            var requestedTenantId = request.TenantId;

            // Validate client exists
            if (!await HasOidcClientConfigurationAsync(request.ClientId))
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = $"OIDC client '{request.ClientId}' not found or not configured" });

            // Look up user and verify credentials (no tenant scoping on initial lookup, like embedded login)
            var user = await _authenticationRepository.GetUserByUsernameAsync(request.Username);
            if (user == null || !user.Active || !user.IsVerified)
                return new UnauthorizedObjectResult(new { error = "invalid_credentials" });

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
                return new ObjectResult(new { error = "account_locked" }) { StatusCode = 423 };

            bool passwordValid;
            try
            {
                passwordValid = BCryptNet.Verify(request.Password, user.Password ?? string.Empty);
            }
            catch
            {
                passwordValid = false;
            }

            if (!passwordValid)
                return new UnauthorizedObjectResult(new { error = "invalid_credentials" });

            // Check if user has multiple tenant access - if so, return account selector
            var userTenants = user.OrganizationIds ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(requestedTenantId))
            {
                // Tenant was specified in request, use it directly
                userTenants = new List<string> { requestedTenantId };
            }
            else if (userTenants.Count > 1)
            {
                // Multiple tenants available - need account selection
                // Create IDP session with all tenant accounts
                var currentSessionId = httpRequest.Cookies[IdpSessionCookieName];
                await EnsureIdpSessionAsync(httpRequest, httpResponse, currentSessionId, user.ItemId, userTenants);

                // Return account selection response
                var accounts = new List<OidcAccountInfo>();
                foreach (var tenantId in userTenants)
                {
                    accounts.Add(new OidcAccountInfo
                    {
                        UserId = user.ItemId,
                        TenantId = tenantId,
                        Email = user.Email ?? request.Username,
                        DisplayName = user.FirstName,
                        TenantName = tenantId // Could lookup tenant name if needed
                    });
                }

                return new OkObjectResult(new OidcLoginResponse
                {
                    Status = "account_selection_required",
                    Accounts = accounts
                });
            }

            var resolvedTenantId = userTenants.FirstOrDefault() ?? string.Empty;

            // Single tenant - proceed with auth code flow
            // Establish IDP session (sets idp_session_id cookie)
            var currentSessionId2 = httpRequest.Cookies[IdpSessionCookieName];
            await EnsureIdpSessionAsync(httpRequest, httpResponse, currentSessionId2, user.ItemId, resolvedTenantId);

            // Create claims principal with authenticated user (don't rely on cookie in same request)
            var claims = new[]
            {
                new Claim("sub", user.ItemId),
                new Claim("tenant_id", resolvedTenantId)
            };
            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            // Issue the authorization code directly (skip login UI)
            return await AuthorizeAsync(
                request.ClientId,
                "code",
                request.RedirectUri,
                request.Scope ?? "openid profile email offline_access",
                request.State ?? string.Empty,
                request.Nonce ?? string.Empty,
                request.CodeChallenge ?? string.Empty,
                request.CodeChallengeMethod ?? "S256",
                null,
                resolvedTenantId,
                principal,
                httpRequest,
                httpResponse,
                false);
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
            ClaimsPrincipal userPrincipal,
            HttpRequest request,
            HttpResponse response,
            bool returnRedirectResponse = true)
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

                var validationResult = _authorizeValidator.Validate(authorizeRequest);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"Authorization request validation failed for {client_id}: {string.Join(", ", validationResult.Errors)}");

                    var errorParams = new Dictionary<string, string>
                    {
                        { "error", "invalid_request" },
                        { "error_description", string.Join("; ", validationResult.Errors) },
                        { "state", state }
                    };

                    if (returnRedirectResponse && !string.IsNullOrWhiteSpace(redirect_uri))
                    {
                        return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                    }

                    return new BadRequestObjectResult(new
                    {
                        error = "invalid_request",
                        error_description = string.Join("; ", validationResult.Errors)
                    });
                }

                var tenantHint = tenant_id;

                var claimUserId = userPrincipal.FindFirst("sub")?.Value;
                var claimTenantId = userPrincipal.FindFirst("tenant_id")?.Value;
                var effectiveSessionId = request.Cookies[IdpSessionCookieName];

                string? resolvedUserId = null;
                string? resolvedTenantId = null;

                if (!string.IsNullOrWhiteSpace(effectiveSessionId))
                {
                    var session = await _sessionRepo.GetBySessionIdAsync(effectiveSessionId);
                    if (session != null && !session.RevokedAt.HasValue && !session.IsExpired())
                    {
                        var sessionAccounts = session.Accounts.AsEnumerable();
                        if (!string.IsNullOrWhiteSpace(tenantHint))
                        {
                            sessionAccounts = sessionAccounts.Where(a => string.Equals(a.TenantId, tenantHint, StringComparison.OrdinalIgnoreCase));
                        }

                        var filteredAccounts = sessionAccounts.ToList();
                        var pendingSelectedUserId = request.Cookies[PendingSelectedUserCookieName];
                        var pendingSelectedTenantId = request.Cookies[PendingSelectedTenantCookieName];

                        if (!string.IsNullOrWhiteSpace(pendingSelectedUserId))
                        {
                            var selectedAccount = filteredAccounts.FirstOrDefault(a =>
                                string.Equals(a.UserId, pendingSelectedUserId, StringComparison.OrdinalIgnoreCase)
                                && (string.IsNullOrWhiteSpace(pendingSelectedTenantId)
                                    || string.Equals(a.TenantId, pendingSelectedTenantId, StringComparison.OrdinalIgnoreCase)));

                            if (selectedAccount == null)
                            {
                                ClearPendingSelectedAccountCookies(response);

                                if (returnRedirectResponse)
                                {
                                    var errorParams = new Dictionary<string, string>
                                    {
                                        { "error", "invalid_request" },
                                        { "error_description", "Selected account is not available in this session" },
                                        { "state", state }
                                    };

                                    return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                                }

                                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Selected account is not available in this session" });
                            }

                            resolvedUserId = selectedAccount.UserId;
                            resolvedTenantId = selectedAccount.TenantId;
                            ClearPendingSelectedAccountCookies(response);
                        }
                        else if (filteredAccounts.Count > 1 || string.Equals(prompt, "select_account", StringComparison.OrdinalIgnoreCase))
                        {
                            await _sessionRepo.UpdateActivityAsync(effectiveSessionId);
                            var chooserUrl = BuildSelectAccountUrl(
                                client_id,
                                response_type,
                                redirect_uri,
                                scope,
                                state,
                                nonce,
                                code_challenge,
                                code_challenge_method,
                                prompt,
                                tenantHint,
                                filteredAccounts);

                            return new RedirectResult(chooserUrl);
                        }
                        else if (filteredAccounts.Count == 1)
                        {
                            resolvedUserId = filteredAccounts[0].UserId;
                            resolvedTenantId = filteredAccounts[0].TenantId;
                            await _sessionRepo.UpdateActivityAsync(effectiveSessionId);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    resolvedUserId = claimUserId;
                    resolvedTenantId = claimTenantId ?? tenantHint;
                }

                if (!string.IsNullOrWhiteSpace(resolvedUserId) && !string.IsNullOrWhiteSpace(resolvedTenantId))
                {
                    await EnsureIdpSessionAsync(request, response, effectiveSessionId, resolvedUserId, resolvedTenantId);
                }

                if (string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    _logger.LogInformation($"Unauthenticated authorization request for {client_id}");
                    return new RedirectResult(BuildLoginUrl(client_id, response_type, redirect_uri, scope, state, nonce, code_challenge, code_challenge_method, tenantHint));
                }

                var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
                if (client == null)
                {
                    _logger.LogWarning($"Unknown client: {client_id}");
                    return new BadRequestObjectResult(new { error = "invalid_client" });
                }

                if (!client.RedirectUris.Contains(redirect_uri))
                {
                    _logger.LogWarning($"Invalid redirect_uri for {client_id}: {redirect_uri}");
                    return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Invalid redirect_uri" });
                }

                canRedirectToClient = true;

                IActionResult BuildAuthorizeError(string error, string errorDescription)
                {
                    if (returnRedirectResponse && canRedirectToClient)
                    {
                        var errorParams = new Dictionary<string, string>
                        {
                            { "error", error },
                            { "error_description", errorDescription },
                            { "state", state }
                        };

                        return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                    }

                    return new BadRequestObjectResult(new
                    {
                        error,
                        error_description = errorDescription
                    });
                }

                if (!await HasOidcClientConfigurationAsync(client_id))
                {
                    _logger.LogWarning($"OIDC client config missing for client: {client_id}");
                    return BuildAuthorizeError("invalid_client", "Client configuration not found");
                }

                var user = await _userRepository.GetUserByIdAsync(resolvedUserId);
                if (user == null)
                {
                    return BuildAuthorizeError("access_denied", "User not found");
                }

                var effectiveOrganizationId = ResolveEffectiveOrganizationId(user);
                await PersistLastUsedOrganizationAsync(user, effectiveOrganizationId);

                var authCode = GenerateRandomCode(32);
                var codeModel = new AuthorizationCodeModel
                {
                    Code = authCode,
                    ClientId = client_id,
                    TenantId = resolvedTenantId,
                    UserId = resolvedUserId,
                    OrganizationId = effectiveOrganizationId,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    Nonce = nonce,
                    State = state,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIpAddress = GetClientIpAddress(request),
                    IsUsed = false
                };

                await _authCodeRepo.CreateAsync(codeModel);

                _logger.LogInformation($"Authorization code issued for user {resolvedUserId}, client {client_id}");

                var callbackParams = new Dictionary<string, string>
                {
                    { "code", authCode },
                    { "state", state },
                    { "tenant_id", resolvedTenantId ?? tenant_id ?? string.Empty }
                };

                var callbackUri = BuildRedirectUri(redirect_uri, callbackParams);

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

                    return new RedirectResult(BuildRedirectUri(redirect_uri, errorParams));
                }

                return new ObjectResult(new { error = "server_error", error_description = "Internal server error" })
                {
                    StatusCode = 500
                };
            }
        }

        public async Task<IActionResult> SelectAccountAsync(string userId, string? tenantId, HttpRequest request, HttpResponse response)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "user_id is required" });
            }

            var effectiveSessionId = request.Cookies[IdpSessionCookieName];
            if (string.IsNullOrWhiteSpace(effectiveSessionId))
            {
                return new ObjectResult(new { error = "session_not_found" }) { StatusCode = 401 };
            }

            var session = await _sessionRepo.GetBySessionIdAsync(effectiveSessionId);
            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                return new ObjectResult(new { error = "session_not_found" }) { StatusCode = 401 };
            }

            var selectedAccount = session.Accounts.FirstOrDefault(a =>
                string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(tenantId)
                    || string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)));

            if (selectedAccount == null)
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Selected account is not available in this session" });
            }

            var isLocal = DomainResolver.IsLocalhost();
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddMinutes(5)
            };

            response.Cookies.Append(PendingSelectedUserCookieName, selectedAccount.UserId, cookieOptions);
            response.Cookies.Append(PendingSelectedTenantCookieName, selectedAccount.TenantId, cookieOptions);

            return new OkObjectResult(new { success = true });
        }

        public async Task<IActionResult> ContinueOidcLoginAfterAccountSelectionAsync(string userId, string tenantId, string clientId, string redirectUri, string? scope, string? state, string? nonce, string? codeChallenge, string? codeChallengeMethod, HttpRequest request, HttpResponse response)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "user_id, tenant_id, and client_id are required" });
            }

            var effectiveSessionId = request.Cookies[IdpSessionCookieName];
            if (string.IsNullOrWhiteSpace(effectiveSessionId))
            {
                return new ObjectResult(new { error = "session_not_found" }) { StatusCode = 401 };
            }

            var session = await _sessionRepo.GetBySessionIdAsync(effectiveSessionId);
            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                return new ObjectResult(new { error = "session_not_found" }) { StatusCode = 401 };
            }

            // Verify account exists in session
            var selectedAccount = session.Accounts.FirstOrDefault(a =>
                string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

            if (selectedAccount == null)
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Selected account is not available in this session" });
            }

            // Create claims principal from selected account
            var claims = new[]
            {
                new Claim("sub", userId),
                new Claim("tenant_id", tenantId)
            };
            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            // Issue authorization code
            return await AuthorizeAsync(
                clientId,
                "code",
                redirectUri,
                scope ?? "openid profile email offline_access",
                state ?? string.Empty,
                nonce ?? string.Empty,
                codeChallenge ?? string.Empty,
                codeChallengeMethod ?? "S256",
                null,
                tenantId,
                principal,
                request,
                response,
                false);
        }

        public async Task<IActionResult> TokenAsync(string grantType, HttpRequest request)
        {
            try
            {
                if (grantType == "authorization_code")
                {
                    return await ExchangeAuthorizationCode(request);
                }

                if (grantType == "refresh_token")
                {
                    return await RotateRefreshToken(request);
                }

                if (grantType == "client_credentials")
                {
                    return await IssueClientCredentialsToken(request);
                }

                return new BadRequestObjectResult(new { error = "unsupported_grant_type", error_description = $"Grant type '{grantType}' not supported" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in token endpoint");
                return new ObjectResult(new { error = "server_error" }) { StatusCode = 500 };
            }
        }

        private async Task<IActionResult> IssueClientCredentialsToken(HttpRequest request)
        {
            var clientId = request.Form["client_id"].ToString();
            var clientSecret = request.Form["client_secret"].ToString();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                TryReadBasicClientAuthentication(request, out clientId, out clientSecret);
            }

            var organizationId = request.Form["organization_id"].ToString();
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                organizationId = request.Form["org_id"].ToString();
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "Missing client authentication" });
            }

            if (!await HasOidcClientConfigurationAsync(clientId))
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (authConfiguration == null)
            {
                return new BadRequestObjectResult(new { error = "server_error", error_description = "Authentication configuration missing" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.ClientCredential,
                ClientId = clientId,
                ClientSecret = clientSecret,
                OrganizationId = organizationId,
                Request = request
            };

            var result = await _clientCredentialAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                var statusCode = string.Equals(result.Error, "invalid_client", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status400BadRequest;

                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                })
                { StatusCode = statusCode };
            }

            return new OkObjectResult(new TokenResponse
            {
                AccessToken = result.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = result.ExpiresIn
            });
        }

        #region OIDC Exchange (Reusable API Block)

        private async Task<IActionResult> ExchangeAuthorizationCode(HttpRequest request)
        {
            var code = request.Form["code"].ToString();
            var code_verifier = request.Form["code_verifier"].ToString();
            var client_id = request.Form["client_id"].ToString();
            var redirect_uri = request.Form["redirect_uri"].ToString();

            // Tenant ID resolution: form > query > header (X-Blocks-Key)
            var tenant_id = !string.IsNullOrWhiteSpace(request.Form["tenant_id"].ToString())
                ? request.Form["tenant_id"].ToString()
                : (!string.IsNullOrWhiteSpace(request.Query["tenant_id"].ToString())
                    ? request.Query["tenant_id"].ToString()
                    : (request.Headers.TryGetValue("X-Blocks-Key", out var headerValue)
                        ? headerValue.ToString()
                        : string.Empty));

            return await ExchangeAuthorizationCodeCore(code, code_verifier, client_id, redirect_uri, tenant_id, request, request.HttpContext.Response);
        }

        // Orchestrator: issue token set, write cookies, then return metadata-only response.
        private async Task<IActionResult> ExchangeAuthorizationCodeCore(string code, string code_verifier, string client_id, string redirect_uri, string tenant_id, HttpRequest request, HttpResponse response)
        {
            var exchangeResult = await ExchangeAuthorizationCodeToTokenSetAsync(code, code_verifier, client_id, redirect_uri, tenant_id, request);
            if (exchangeResult.ErrorResult != null)
            {
                return exchangeResult.ErrorResult;
            }

            // Get client registration to check token delivery mode
            var clientRegistration = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
            var useTokensCookie = clientRegistration?.UseTokensCookie ?? true;

            // Validate tokens are present before proceeding
            if (string.IsNullOrWhiteSpace(exchangeResult.AccessToken))
            {
                _logger.LogError($"Access token generation failed for client {client_id}");
                return new BadRequestObjectResult(new { error = "server_error", error_description = "Failed to generate access token" });
            }

            if (useTokensCookie && exchangeResult.CanSetCookies)
            {
                var cookieDomain = exchangeResult.CookieDomain;
                var tokenDomain = exchangeResult.Domain ?? exchangeResult.EffectiveTenantId;
                var cookiesSet = AppendAccessAndRefreshTokenCookies(
                    response,
                    tokenDomain,
                    exchangeResult.AccessToken,
                    exchangeResult.RefreshToken,
                    cookieDomain,
                    exchangeResult.AccessExpiry,
                    exchangeResult.RefreshExpiry);
                
                if (!cookiesSet)
                {
                    _logger.LogWarning($"Failed to set authentication cookies for client {client_id}, domain {tokenDomain}. Falling back to token response body.");
                    // Fallback: return tokens in response body instead of cookies
                    return new OkObjectResult(new
                    {
                        access_token = exchangeResult.AccessToken,
                        id_token = exchangeResult.IdToken,
                        refresh_token = exchangeResult.RefreshToken,
                        token_type = "Bearer",
                        expires_in = exchangeResult.ExpiresIn,
                        scope = exchangeResult.Scope,
                        cookie_delivery_failed = true
                    });
                }
                
                return new OkObjectResult(new
                {
                    id_token = exchangeResult.IdToken,
                    token_type = "Bearer",
                    expires_in = exchangeResult.ExpiresIn,
                    scope = exchangeResult.Scope,
                    cookie_set = true
                });
            }

            // Fallback: client not configured for cookie-based token delivery or domain resolution failed
            if (useTokensCookie && !exchangeResult.CanSetCookies)
            {
                _logger.LogWarning($"Cannot set cookies for client {client_id}: domain resolution failed. Returning tokens in response body.");
            }

            return new OkObjectResult(new
            {
                access_token = exchangeResult.AccessToken,
                id_token = exchangeResult.IdToken,
                refresh_token = exchangeResult.RefreshToken,
                token_type = "Bearer",
                expires_in = exchangeResult.ExpiresIn,
                scope = exchangeResult.Scope,
                cookie_set = false
            });
            
        }

        // Grouped issuance block: validate code + PKCE + client, then build access/id/refresh token set.
        private async Task<OidcExchangeResult> ExchangeAuthorizationCodeToTokenSetAsync(string code, string code_verifier, string client_id, string redirect_uri, string tenant_id, HttpRequest request)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(client_id))
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing required parameters" }));
            }

            var tenantId = !string.IsNullOrWhiteSpace(tenant_id)
                ? tenant_id
                : request.HttpContext.User.FindFirst("tenant_id")?.Value;

            var authCode = await _authCodeRepo.GetByCodeAsync(code);
            if (authCode == null)
            {
                _logger.LogWarning($"Authorization code not found: {code}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired" }));
            }

            if (!string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(authCode.TenantId)
                && !string.Equals(authCode.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"Tenant mismatch for code exchange. Presented tenant: {tenantId}, code tenant: {authCode.TenantId}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Tenant mismatch" }));
            }

            if (authCode.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning($"Authorization code expired: {code}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code has expired" }));
            }

            if (authCode.IsUsed)
            {
                _logger.LogCritical($"REUSE ATTACK DETECTED: Code reused by IP {GetClientIpAddress(request)}, original IP {authCode.UsedByIpAddress}. Revoking token family.");
                await RevokeUserTokens(authCode.UserId, authCode.ClientId, authCode.TenantId ?? tenantId ?? string.Empty);
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code has already been used" }));
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);
            if (client == null || client.ClientId != authCode.ClientId)
            {
                _logger.LogWarning("Client validation failed for code exchange");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client" }));
            }

            if (!await HasOidcClientConfigurationAsync(client_id))
            {
                _logger.LogWarning($"OIDC client config missing for code exchange: {client_id}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" }));
            }

            if (authCode.RedirectUri != redirect_uri)
            {
                _logger.LogWarning("Redirect URI mismatch for code exchange");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Redirect URI mismatch" }));
            }

            if (!string.IsNullOrWhiteSpace(code_verifier))
            {
                var pkceValid = await _pkceService.ValidateVerifierAsync(authCode.CodeChallenge, code_verifier, authCode.CodeChallengeMethod);
                if (!pkceValid)
                {
                    _logger.LogWarning($"PKCE validation failed for client {client_id}");
                    return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "PKCE code_verifier is invalid" }));
                }
            }


            var markUsedSuccess = await _authCodeRepo.MarkAsUsedAsync(code, DateTime.UtcNow, GetClientIpAddress(request));
            if (!markUsedSuccess)
            {
                _logger.LogWarning($"Failed to mark authorization code as used: {code}");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Could not process authorization code" }));
            }

            var user = await _userRepository.GetUserByIdAsync(authCode.UserId);
            if (user == null)
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "User not found" }));
            }
            var effectiveTenantId = authCode.TenantId ?? tenant_id ?? "default";
            var tenant = _tenants.GetTenantByID(effectiveTenantId);
            var allowedScopes = await ResolveAllowedScopesAsync(client);
            var allowedServiceAccessResources = await ResolveAllowedServiceAccessResourcesAsync(client.ClientId);
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                user,
                authCode.OrganizationId,
                authCode.Scope,
                allowedServiceAccessResources,
                requireExplicitScope: true);

            var tenantAudience = DomainResolver.GetAudience(tenant);

            var claims = new OidcClaims
            {
                Sub = authCode.UserId,
                TenantId = effectiveTenantId,
                OrgId = authCode.OrganizationId,
                Nonce = authCode.Nonce,
                AuthTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id,
                Audience = tenantAudience,
                Scope = authCode.Scope,
                Roles = resolvedClaims.Roles,
                Resources = resolvedClaims.Resources,
                Permissions = resolvedClaims.Permissions
            };

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var accessTokenLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? AuthenticationConfiguration.DefaultAccessTokenValidForNumberMinutes) * 60, 60);
            var absoluteRefreshTokenLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? AuthenticationConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

            var issuer = DomainResolver.GetIssuer(tenant);
            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer);

            refreshTokenModel.UserId = authCode.UserId;
            refreshTokenModel.ClientId = client_id;
            refreshTokenModel.TenantId = effectiveTenantId;
            refreshTokenModel.OrgId = authCode.OrganizationId;
            refreshTokenModel.Audience = tenantAudience;
            refreshTokenModel.Scope = authCode.Scope;
            refreshTokenModel.IpAddress = GetClientIpAddress(request);
            refreshTokenModel.UserAgent = request.Headers["User-Agent"].ToString();
            await _refreshTokenRepo.CreateAsync(refreshTokenModel);

            _logger.LogInformation($"Tokens issued for user {authCode.UserId}, client {client_id}");

            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            var accessExpiry = DateTime.UtcNow.AddSeconds(accessTokenLifetimeSeconds);
            var refreshExpiry = refreshTokenModel.AbsoluteExpiry == default
                ? DateTime.UtcNow.AddMinutes(absoluteRefreshTokenLifetimeMinutes)
                : refreshTokenModel.AbsoluteExpiry;

            return OidcExchangeResult.FromTokens(
                accessToken,
                idToken,
                refreshTokenModel.TokenId,
                effectiveTenantId,
                isResolved ? domain : null,
                DomainResolver.IsLocalhost() ? null : cookieDomain,
                authCode.Scope,
                accessTokenLifetimeSeconds,
                accessExpiry,
                refreshExpiry);
        }

        private static bool AppendAccessAndRefreshTokenCookies(
            HttpResponse response,
            string tokenDomain,
            string? accessToken,
            string? refreshToken,
            string? cookieDomain,
            DateTime accessExpiry,
            DateTime refreshExpiry)
        {
            // Validate tokens are not empty before attempting to set cookies
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false; // Cannot set cookies without valid access token
            }

            var isLocal = DomainResolver.IsLocalhost();
            cookieDomain = isLocal ? null : (string.IsNullOrWhiteSpace(cookieDomain) ? null : cookieDomain);
            var accessOptions = new CookieOptions {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = true,
                SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = accessExpiry
            };
            var refreshOptions = new CookieOptions {
                Domain = cookieDomain,
                HttpOnly = true,
                Secure = true,
                SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = refreshExpiry
            };

            response.Cookies.Append($"{tokenDomain}", accessToken, accessOptions);
            
            // Only append refresh token if provided
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tokenDomain}", refreshToken, refreshOptions);
            }

            return true;
        }

        // Internal transport model for exchange outcome: either error result or issued token set.
        private sealed class OidcExchangeResult
        {
            public IActionResult? ErrorResult { get; private set; }
            public string AccessToken { get; private set; } = string.Empty;
            public string IdToken { get; private set; } = string.Empty;
            public string RefreshToken { get; private set; } = string.Empty;
            public string EffectiveTenantId { get; private set; } = string.Empty;
            public string? Domain { get; private set; }
            public string? CookieDomain { get; private set; }
            public bool CanSetCookies => !string.IsNullOrWhiteSpace(Domain);
            public string Scope { get; private set; } = string.Empty;
            public int ExpiresIn { get; private set; }
            public DateTime AccessExpiry { get; private set; }
            public DateTime RefreshExpiry { get; private set; }

            public static OidcExchangeResult FromError(IActionResult errorResult)
            {
                return new OidcExchangeResult { ErrorResult = errorResult };
            }

            public static OidcExchangeResult FromTokens(
                string accessToken,
                string idToken,
                string refreshToken,
                string effectiveTenantId,
                string? domain,
                string? cookieDomain,
                string scope,
                int expiresIn,
                DateTime accessExpiry,
                DateTime refreshExpiry)
            {
                return new OidcExchangeResult
                {
                    AccessToken = accessToken,
                    IdToken = idToken,
                    RefreshToken = refreshToken,
                    EffectiveTenantId = effectiveTenantId,
                    Domain = domain,
                    CookieDomain = cookieDomain,
                    Scope = scope,
                    ExpiresIn = expiresIn,
                    AccessExpiry = accessExpiry,
                    RefreshExpiry = refreshExpiry
                };
            }
        }

        #endregion

        private async Task<IActionResult> RotateRefreshToken(HttpRequest request)
        {
            var client_id = request.Form["client_id"].ToString();
            string refresh_token = "";

            if (string.IsNullOrEmpty(client_id))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing client_id" });
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(client_id);

            if (client is null)
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "client not found" });
            }
            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "default");

            if (client.UseTokensCookie)
            {
                var (domain, _, isResolved) = DomainResolver.ResolveDomain(tenant, request);
                var cookieKey = isResolved && !string.IsNullOrWhiteSpace(domain)
                    ? $"{IdpConstants.RefreshTokenCookieName}_{domain}"
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(cookieKey))
                {
                    refresh_token = request.HttpContext.Request.Cookies[cookieKey] ?? string.Empty;
                }

                // For API/postman callers (or unresolved domain), accept body token as runtime fallback.
                if (string.IsNullOrWhiteSpace(refresh_token))
                {
                    refresh_token = request.Form["refresh_token"].ToString();
                }
            }
            else
            {
                refresh_token = request.Form["refresh_token"].ToString();
            }

            if (string.IsNullOrWhiteSpace(refresh_token))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "refresh token not found" });
            }

            var storedToken = await _refreshTokenRepo.GetByTokenIdAsync(refresh_token);
            if (storedToken == null)
            {
                _logger.LogWarning($"Refresh token not found: {refresh_token}");
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Invalid refresh token" });
            }

            if (storedToken.IsRevoked)
            {
                _logger.LogCritical($"REUSE ATTACK DETECTED: Revoked token used again. Original revocation reason: {storedToken.RevokeReason}. Revoking token {storedToken.TokenId}.");
                await _refreshTokenRepo.RevokeByTokenIdAsync(storedToken.TokenId, "reuse_detected");
                await LogAuditEvent("token_reuse_detected", storedToken.UserId, client_id, storedToken.TenantId ?? tenant?.ItemId ?? "default", "CRITICAL", request);
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token has been revoked" });
            }

            if (storedToken.IsExpired())
            {
                _logger.LogWarning($"Refresh token expired: {refresh_token}");
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token has expired" });
            }

            if (!await HasOidcClientConfigurationAsync(client_id))
            {
                _logger.LogWarning($"OIDC client config missing for token rotation: {client_id}");
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" });
            }

            if (!string.IsNullOrWhiteSpace(storedToken.ClientId) && !string.Equals(storedToken.ClientId, client_id, StringComparison.Ordinal))
            {
                _logger.LogWarning($"Refresh token client mismatch. Presented client: {client_id}, token client: {storedToken.ClientId}");
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token does not belong to this client" });
            }

            var user = await _userRepository.GetUserByIdAsync(storedToken.UserId);
            if (user == null)
            {
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "User not found" });
            }

            var allowedScopes = await ResolveAllowedScopesAsync(client);
            var allowedServiceAccessResources = await ResolveAllowedServiceAccessResourcesAsync(client.ClientId);
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                user,
                storedToken.OrgId,
                storedToken.Scope,
                allowedServiceAccessResources,
                requireExplicitScope: true);

            var tenantAudience = DomainResolver.GetAudience(tenant);

            var claims = new OidcClaims
            {
                Sub = storedToken.UserId,
                TenantId = storedToken.TenantId,
                OrgId = storedToken.OrgId,
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id,
                Audience = tenantAudience,
                Scope = storedToken.Scope,
                Roles = resolvedClaims.Roles,
                Resources = resolvedClaims.Resources,
                Permissions = resolvedClaims.Permissions
            };

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var accessTokenLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? AuthenticationConfiguration.DefaultAccessTokenValidForNumberMinutes) * 60, 60);
            var issuer = DomainResolver.GetIssuer(tenant);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var newRefreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer);

            newRefreshTokenModel.UserId = storedToken.UserId;
            newRefreshTokenModel.ClientId = client_id;
            newRefreshTokenModel.TenantId = storedToken.TenantId ?? string.Empty;
            newRefreshTokenModel.OrgId = storedToken.OrgId;
            newRefreshTokenModel.Audience = tenantAudience;
            newRefreshTokenModel.Scope = storedToken.Scope;
            newRefreshTokenModel.SessionId = storedToken.SessionId;
            newRefreshTokenModel.IpAddress = GetClientIpAddress(request);
            newRefreshTokenModel.UserAgent = request.Headers["User-Agent"].ToString();

            await _refreshTokenRepo.CreateAsync(newRefreshTokenModel);

            await _refreshTokenRepo.RevokeByTokenIdAsync(storedToken.TokenId, "rotated");

            _logger.LogInformation($"Token rotated for user {storedToken.UserId}, client {client_id}");
            await LogAuditEvent("token_refreshed", storedToken.UserId, client_id, storedToken.TenantId ?? string.Empty, "INFO", request);
            var effectiveTenantId = storedToken.TenantId ?? tenant?.ItemId ?? "default";

            if (client.UseTokensCookie)
            {
                var (resolvedDomain, resolvedCookieDomain, resolvedByDomain) = DomainResolver.ResolveDomain(tenant, request);
                var adjustedCookieDomain = DomainResolver.IsLocalhost() ? null : resolvedCookieDomain;
                var absoluteRefreshTokenLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? AuthenticationConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);
                var accessExpiry = DateTime.UtcNow.AddSeconds(accessTokenLifetimeSeconds);
                var refreshExpiry = newRefreshTokenModel.AbsoluteExpiry == default
                    ? DateTime.UtcNow.AddMinutes(absoluteRefreshTokenLifetimeMinutes)
                    : newRefreshTokenModel.AbsoluteExpiry;

                if (resolvedByDomain && !string.IsNullOrWhiteSpace(resolvedDomain))
                {
                    AppendAccessAndRefreshTokenCookies(
                        request.HttpContext.Response,
                        resolvedDomain,
                        accessToken,
                        newRefreshTokenModel.TokenId,
                        adjustedCookieDomain,
                        accessExpiry,
                        refreshExpiry);

                    return new OkObjectResult(new
                    {
                        token_type = "Bearer",
                        expires_in = accessTokenLifetimeSeconds,
                        scope = "openid profile email",
                    });
                }
            }

            return new OkObjectResult(new
            {
                access_token = accessToken,
                refresh_token = newRefreshTokenModel.TokenId,
                token_type = "Bearer",
                expires_in = accessTokenLifetimeSeconds,
                scope = "openid profile email",
            });
        }

        private async Task LogAuditEvent(string eventType, string userId, string clientId, string tenantId, string severity, HttpRequest request)
        {
            try
            {
                var auditLog = new AuditLogModel
                {
                    EventType = eventType,
                    UserId = userId,
                    ClientId = clientId,
                    TenantId = tenantId,
                    IpAddress = GetClientIpAddress(request),
                    UserAgent = request.Headers["User-Agent"].ToString(),
                    Severity = severity,
                    Status = "success",
                    Timestamp = DateTime.UtcNow
                };
                await _auditLogRepo.CreateAsync(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error logging audit event: {eventType}");
            }
        }

        private static string GenerateRandomCode(int length)
        {
            byte[] buffer = new byte[length];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToBase64String(buffer).Replace("/", "_").Replace("+", "-").Substring(0, 43);
        }

        private async Task<IReadOnlyCollection<string>> ResolveAllowedScopesAsync(OidcClientRegistration client)
        {
            if (client.AllowedScopes is { Count: > 0 })
            {
                return client.AllowedScopes
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return [];
        }

        private async Task<IReadOnlyCollection<string>> ResolveAllowedServiceAccessResourcesAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return [];
            }

            var tenantOidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            if (tenantOidcClient == null || tenantOidcClient.AllowedServiceAccessResources.Count == 0)
            {
                return [];
            }

            return tenantOidcClient.AllowedServiceAccessResources
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildRedirectUri(string baseUri, Dictionary<string, string> parameters)
        {
            var sb = new StringBuilder(baseUri);
            sb.Append(baseUri.Contains("?") ? "&" : "?");

            foreach (var param in parameters.Where(p => !string.IsNullOrEmpty(p.Value)))
            {
                sb.Append(Uri.EscapeDataString(param.Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(param.Value));
                sb.Append("&");
            }

            return sb.ToString().TrimEnd('&');
        }

        private static string BuildLoginUrl(
            string clientId,
            string responseType,
            string redirectUri,
            string scope,
            string state,
            string nonce,
            string codeChallenge,
            string codeChallengeMethod,
            string? tenantId)
        {
            var loginUrl = new StringBuilder("/oidc/login?");
            loginUrl.Append($"client_id={Uri.EscapeDataString(clientId ?? string.Empty)}");
            loginUrl.Append($"&response_type={Uri.EscapeDataString(responseType ?? string.Empty)}");
            loginUrl.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri ?? string.Empty)}");
            loginUrl.Append($"&scope={Uri.EscapeDataString(scope ?? string.Empty)}");
            loginUrl.Append($"&state={Uri.EscapeDataString(state ?? string.Empty)}");
            loginUrl.Append($"&nonce={Uri.EscapeDataString(nonce ?? string.Empty)}");
            loginUrl.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}");
            loginUrl.Append($"&code_challenge_method={Uri.EscapeDataString(codeChallengeMethod ?? string.Empty)}");

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                loginUrl.Append($"&tenant_id={Uri.EscapeDataString(tenantId)}");
            }

            return loginUrl.ToString();
        }

        private static void TryReadBasicClientAuthentication(HttpRequest request, out string clientId, out string clientSecret)
        {
            clientId = string.Empty;
            clientSecret = string.Empty;

            if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authHeader)
                || !string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(authHeader.Parameter))
            {
                return;
            }

            try
            {
                var rawCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Parameter));
                var separatorIndex = rawCredentials.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    return;
                }

                clientId = rawCredentials[..separatorIndex];
                clientSecret = rawCredentials[(separatorIndex + 1)..];
            }
            catch
            {
                clientId = string.Empty;
                clientSecret = string.Empty;
            }
        }

        private static string BuildSelectAccountUrl(
            string clientId,
            string responseType,
            string redirectUri,
            string scope,
            string state,
            string nonce,
            string codeChallenge,
            string codeChallengeMethod,
            string? prompt,
            string? tenantId,
            List<IdpSessionAccount> accounts)
        {
            var chooserUrl = new StringBuilder("/oidc/select-account?");
            chooserUrl.Append($"client_id={Uri.EscapeDataString(clientId ?? string.Empty)}");
            chooserUrl.Append($"&response_type={Uri.EscapeDataString(responseType ?? string.Empty)}");
            chooserUrl.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri ?? string.Empty)}");
            chooserUrl.Append($"&scope={Uri.EscapeDataString(scope ?? string.Empty)}");
            chooserUrl.Append($"&state={Uri.EscapeDataString(state ?? string.Empty)}");
            chooserUrl.Append($"&nonce={Uri.EscapeDataString(nonce ?? string.Empty)}");
            chooserUrl.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}");
            chooserUrl.Append($"&code_challenge_method={Uri.EscapeDataString(codeChallengeMethod ?? string.Empty)}");

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                chooserUrl.Append($"&prompt={Uri.EscapeDataString(prompt)}");
            }

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                chooserUrl.Append($"&tenant_id={Uri.EscapeDataString(tenantId)}");
            }

            foreach (var account in accounts)
            {
                var payload = $"{account.UserId}|{account.TenantId}|{account.DisplayName ?? string.Empty}";
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                chooserUrl.Append($"&acct={Uri.EscapeDataString(encoded)}");
            }

            return chooserUrl.ToString();
        }

        private static void ClearPendingSelectedAccountCookies(HttpResponse response)
        {
            response.Cookies.Delete(PendingSelectedUserCookieName);
            response.Cookies.Delete(PendingSelectedTenantCookieName);
        }

        private async Task EnsureIdpSessionAsync(HttpRequest request, HttpResponse response, string? currentSessionId, string userId, string tenantId)
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
                    IpAddress = GetClientIpAddress(request),
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    IdleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout()),
                    AbsoluteExpiry = DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                };

                await _sessionRepo.CreateAsync(newSession);
                SetIdpSessionCookie(response, tenantId, newSession.SessionId, newSession.AbsoluteExpiry);
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

            SetIdpSessionCookie(response, tenantId, session.SessionId, session.AbsoluteExpiry);
        }

        private async Task EnsureIdpSessionAsync(HttpRequest request, HttpResponse response, string? currentSessionId, string userId, List<string> tenantIds)
        {
            // Overload for multiple tenants (account selection case)
            var session = string.IsNullOrWhiteSpace(currentSessionId)
                ? null
                : await _sessionRepo.GetBySessionIdAsync(currentSessionId);

            var primaryTenantId = tenantIds.FirstOrDefault() ?? string.Empty;

            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                var accounts = new List<IdpSessionAccount>();
                foreach (var tenantId in tenantIds)
                {
                    accounts.Add(new IdpSessionAccount
                    {
                        UserId = userId,
                        TenantId = tenantId,
                        DisplayName = userId,
                        LoginAt = DateTime.UtcNow
                    });
                }

                var newSession = new IdpSessionModel
                {
                    SessionId = Guid.NewGuid().ToString("n"),
                    TenantId = primaryTenantId,
                    Accounts = accounts,
                    IpAddress = GetClientIpAddress(request),
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    IdleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout()),
                    AbsoluteExpiry = DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                };

                await _sessionRepo.CreateAsync(newSession);
                SetIdpSessionCookie(response, primaryTenantId, newSession.SessionId, newSession.AbsoluteExpiry);
                return;
            }

            // Add missing tenant accounts to existing session
            foreach (var tenantId in tenantIds)
            {
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
            }

            await _sessionRepo.UpdateActivityAsync(session.SessionId);
            SetIdpSessionCookie(response, primaryTenantId, session.SessionId, session.AbsoluteExpiry);
        }

        private void SetIdpSessionCookie(HttpResponse response, string tenantId, string sessionId, DateTime absoluteExpiry)
        {
            var tenant = _tenants.GetTenantByID(tenantId);
            var (_, cookieDomain, _) = DomainResolver.ResolveDomain(tenant, null);
            var isLocal = DomainResolver.IsLocalhost();
            var adjustedCookieDomain = isLocal ? null : cookieDomain;
            response.Cookies.Append(IdpSessionCookieName, sessionId, new CookieOptions
            {
                Domain = adjustedCookieDomain,
                HttpOnly = true,
                Secure = true,
                SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
                Path = "/",
                Expires = absoluteExpiry == default ? DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout()) : absoluteExpiry
            });
        }

        private static TimeSpan GetIdpSessionIdleTimeout()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_IDLE_HOURS");
            if (double.TryParse(configured, out var hours) && hours > 0 && hours <= 168)
            {
                return TimeSpan.FromHours(hours);
            }

            return TimeSpan.FromHours(24);
        }

        private static TimeSpan GetIdpSessionAbsoluteTimeout()
        {
            var configured = Environment.GetEnvironmentVariable("IDP_SESSION_ABSOLUTE_HOURS");
            if (double.TryParse(configured, out var hours) && hours > 0 && hours <= 168)
            {
                return TimeSpan.FromHours(hours);
            }

            return TimeSpan.FromHours(5); // Default to 5 hours
        }

        private static string? ResolveEffectiveOrganizationId(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.LastUsedOrganizationId)
                && HasOrganizationAccess(user, user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId;
            }

            if (HasOrganizationAccess(user, "default"))
            {
                return "default";
            }

            return user.OrganizationIds.FirstOrDefault()
                ?? user.Roles.Keys.FirstOrDefault()
                ?? user.Permissions.Keys.FirstOrDefault();
        }

        private static bool HasOrganizationAccess(User user, string organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
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



        private static string GetClientIpAddress(HttpRequest request)
        {
            if (request.HttpContext.Connection.RemoteIpAddress != null)
            {
                return request.HttpContext.Connection.RemoteIpAddress.ToString();
            }
            return "unknown";
        }

        private async Task<bool> HasOidcClientConfigurationAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            var oidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            return oidcClient != null;
        }

        private async Task RevokeUserTokens(string userId, string clientId, string tenantId)
        {
            try
            {
                var userTokens = await _refreshTokenRepo.GetByUserAsync(userId, tenantId);
                var clientTokens = userTokens.Where(t => t.ClientId == clientId && !t.IsRevoked).ToList();

                foreach (var token in clientTokens)
                {
                    await _refreshTokenRepo.RevokeByTokenIdAsync(token.TokenId, "authorization_code_reuse_detected");
                }

                var auditLog = new AuditLogModel
                {
                    EventType = "code_reuse_attack",
                    UserId = userId,
                    ClientId = clientId,
                    TenantId = tenantId,
                    IpAddress = "unknown",
                    UserAgent = "unknown",
                    Severity = "CRITICAL",
                    Status = "success",
                    Timestamp = DateTime.UtcNow
                };
                await _auditLogRepo.CreateAsync(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking user tokens for {userId}");
            }
        }
    }
}
