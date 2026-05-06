using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private static readonly HttpClient BackchannelHttpClient = new();
        private const string IdpSessionCookieName = "idp_session_id";
        private readonly ILogger<AuthenticationService> _logger;
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IIdpSessionService _idpSessionService;
        private readonly ITenants _tenants;

        private const string Public_Cert_Cache_Prefix = "tetocertpublic::";

        public AuthenticationService(
            ILogger<AuthenticationService> logger,
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService,
            IIdpSessionService idpSessionService,
            ITenants tenants
        )
        {
            _logger = logger;
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
            _idpSessionService = idpSessionService;
            _tenants = tenants;
        }

        public async Task<IActionResult> BuildFlowResultAsync(AuthenticationFlowResult result, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                })
                {
                    StatusCode = result.StatusCode
                };
            }

            if (result.TokenResponse == null)
            {
                return new ObjectResult(new
                {
                    error = "server_error",
                    error_description = "Authentication flow returned no response"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            return await BuildTokenResponseAsync(result.TokenResponse, httpContext);
        }

        public async Task<bool> UpdateIdpSessionForLogoutAsync(HttpContext httpContext, ClaimsPrincipal user, bool isGlobalLogout)
        {
            var sessionId = httpContext.Request.Cookies[IdpSessionCookieName];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return true;
            }

            if (isGlobalLogout)
            {
                await _idpSessionService.RevokeSessionAsync(sessionId, "logout_all");
                return true;
            }

            var userId = user.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value ?? user.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            var tenantId = user.FindFirst(BlocksContext.TENANT_ID_CLAIM)?.Value
                ?? user.FindFirst("tenant_id")?.Value
                ?? BlocksContext.GetContext()?.TenantId;

            await _idpSessionService.RemoveAccountAsync(sessionId, userId, tenantId);

            var session = await _idpSessionService.GetSessionAsync(sessionId);
            if (session == null || session.RevokedAt.HasValue || session.IsExpired())
            {
                return true;
            }

            return session.Accounts.Count == 0;
        }

        public void ClearIdpSessionCookie(HttpResponse response)
        {
            response.Cookies.Delete(IdpSessionCookieName);
        }

        public async Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest)
        {
            _logger.LogInformation("Logout process start");

            var isAll = string.IsNullOrWhiteSpace(refreshToken);

            var result = isAll ? await ProcessLogoutAll() : await ProcessLogout(refreshToken);

            await ProcessTimeline(httpRequest, isAll);
            return new LogoutResponse
            {
                IsSuccess = result,
            };

        }

        public async Task<bool> ProcessLogout(string refreshToken)
        {
            await _cacheClient.RemoveKeyAsync(refreshToken);
            var bc = BlocksContext.GetContext();

            var result = await _authenticationRepository.UpdateSessionStatusAsync(refreshToken, bc?.UserId ?? "");

            return result;
        }

        public async Task<bool> ProcessLogoutAll()
        {
            var bc = BlocksContext.GetContext();

            var refreshTokens = (await _authenticationRepository.GetActiveSessionByUserIdAsync(bc.UserId)).Select(x => x.RefreshToken).ToList();
            var cacheTask = refreshTokens.Select(async x => await _cacheClient.RemoveKeyAsync(x));
            await Task.WhenAll(cacheTask);

            var result = await _authenticationRepository.UpdateSessionStatusForAllRefreshTokenAsync(refreshTokens);
            return result;
        }

        public async Task<bool> ProcessTimeline(HttpRequest httpRequest, bool isFromAll)
        {
            var bc = BlocksContext.GetContext();
            var eventTimeline = new UserAuthenticationTimelineEvent
            {
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(httpRequest?.Headers?.UserAgent ?? string.Empty),
                IpAddresses = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(httpRequest?.HttpContext)),
                Event = isFromAll ? "revoke_access_by_logout_all" : "revoke_access_by_logout",
                ActionBy = isFromAll ? "call_api_to_logout_all" : "call_api_to_logout",
                UserId = bc?.UserId?? ""
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, eventTimeline);
            return true;
        }

        public async Task<OidcClientRegistration> GetClientCredentialAsync(string clientId)
        {
            return await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
        }

        public async Task<string> ConstructRedirectUriAsync(string clientId, AcknowledgeRequest request)
        {
            var client = await GetClientCredentialAsync(request.ClientId);
            var code = Guid.NewGuid().ToString("n");
            var nextUrl = client.RedirectUris.FirstOrDefault() ?? string.Empty;
            var serviceAccessResource = client.AllowedServiceAccessResources.FirstOrDefault() ?? string.Empty;
            var stateInfo = new StateInfo { Scope = request.Scope, secret = client.ClientSecret, State = request.State, Code = code, Nonce = request.Nonce, UserName = request.Username, Audience = serviceAccessResource, Provider = "SeliseCloud", NextUrl = nextUrl };
            await _cacheClient.AddStringValueAsync(code, JsonSerializer.Serialize(stateInfo), 300);
            var uri = $"{nextUrl}?code={code}";

            if (!string.IsNullOrEmpty(request.State))
                uri += $"&state={request.State}";

            return uri;
        }

        public string CookieToken(HttpRequest request)
        {
            var bc = BlocksContext.GetContext();  
            var refreshToken = request.HttpContext.Request.Cookies[$"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}"];
            refreshToken = string.IsNullOrEmpty(refreshToken) ? request.HttpContext.Request.Headers[$"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}"] : refreshToken;

            return refreshToken;
        }

        public bool DeleteCookie(HttpRequest request)
        {
            var cookieDomain = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.CookieDomain;
            var bc = BlocksContext.GetContext();    
            var cookieOptions = new CookieOptions
            {
                Domain = cookieDomain, 
                Path = "/",              
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.None
            };

            request.HttpContext.Response.Cookies.Delete($"{IdpConstants.RefreshTokenCookieName}_{bc.TenantId}", cookieOptions);
            request.HttpContext.Response.Cookies.Delete($"{IdpConstants.AccessTokenCookieName}_{bc.TenantId}", cookieOptions);

            return true;
        }

        public async Task<IActionResult> GetLoginOptionsAsync()
        {
            var config = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var ssoConfig = (await _authenticationDomainService.GetSocialLoginCredentialsAsync()).Where(c=>c.IsDisabled == false);

            return new OkObjectResult(new
            {
                AllowedGrantTypes = config.AllowedGrantTypes.Except(["mfa_code", "refresh_token"]),
                SsoInfo = config.AllowedGrantTypes.Contains("social")
                          ? ssoConfig.Select(info => new { info.Provider, info.Audience })
                          : null
            });
        }

        public async Task<bool> TriggerBackchannelLogoutAllAsync(HttpRequest httpRequest)
        {
            var bc = BlocksContext.GetContext();
            var clients = await _authenticationRepository.GetOIDCCredentialsByTenantAsync();
            var backchannelUris = clients
                .Select(client => client.BackChannelLogoutUri)
                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (backchannelUris.Count == 0)
            {
                _logger.LogInformation("No backchannel logout URIs configured for tenant {TenantId}", bc?.TenantId);
                return true;
            }

            var requestPayload = new
            {
                user_id = bc?.UserId,
                tenant_id = bc?.TenantId,
                event_name = "logout_all",
                session_id = httpRequest.Cookies["idp_session_id"],
                occurred_at_utc = DateTime.UtcNow
            };

            var allSucceeded = true;
            foreach (var uri in backchannelUris)
            {
                try
                {
                    var response = await BackchannelHttpClient.PostAsJsonAsync(uri!, requestPayload);
                    if (!response.IsSuccessStatusCode)
                    {
                        allSucceeded = false;
                        _logger.LogWarning("Backchannel logout call failed for {Uri} with status code {StatusCode}", uri, (int)response.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    _logger.LogWarning(ex, "Backchannel logout call threw for {Uri}", uri);
                }
            }

            return allSucceeded;
        }

        public async Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(HttpRequest request, string tenantId, bool IsUserInfoGetRequest = false)
        {
            var (token, _) =  TokenHelper.GetToken(request, _tenants);
            var tenant = _tenants.GetTenantByID(tenantId);
            if (tenant == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(token))
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                string cacheKey = $"{Public_Cert_Cache_Prefix}{tenant.TenantId}";
                var certificateData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
                var validationParams = tenant.JwtTokenParameters;
                var publicCert = X509CertificateLoader.LoadPkcs12(certificateData, validationParams.PublicCertificatePassword);
                var tokenValidationParameters = !IsUserInfoGetRequest? new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidIssuer = validationParams?.Issuer, ValidAudience = TenantDomainPolicy.GetAudience(tenant), ValidateAudience = true, SaveSigninToken = true } :
                                                                      new TokenValidationParameters { ValidateLifetime = true, ClockSkew = TimeSpan.Zero, IssuerSigningKey = new X509SecurityKey(publicCert), ValidateIssuerSigningKey = true, ValidateIssuer = false, ValidateAudience = false, SaveSigninToken = true };
                return tokenHandler.ValidateToken(token, tokenValidationParameters, out _);
            }

            return null;
        }

        public (bool IsValid, Dictionary<string, object> UserInfo) BuildOidcUserInfo(ClaimsPrincipal principal)
        {
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(BlocksContext.USER_ID_CLAIM)?.Value;

            if (string.IsNullOrWhiteSpace(sub))
            {
                return (false, new Dictionary<string, object>());
            }

            var userInfo = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = sub
            };

            var grantedScopes = GetGrantedScopes(principal);

            if (HasScope(grantedScopes, "profile"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "name", BlocksContext.DISPLAY_NAME_CLAIM);
                AddSingleClaimIfPresent(principal, userInfo, "preferred_username", BlocksContext.USER_NAME_CLAIM);
            }

            if (HasScope(grantedScopes, "email"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "email", BlocksContext.EMAIL_CLAIM);
            }

            if (HasScope(grantedScopes, "phone"))
            {
                AddSingleClaimIfPresent(principal, userInfo, "phone_number", BlocksContext.PHONE_NUMBER_CLAIM);
            }

            AddSingleClaimIfPresent(principal, userInfo, BlocksContext.TENANT_ID_CLAIM, BlocksContext.TENANT_ID_CLAIM);
            AddSingleClaimIfPresent(principal, userInfo, BlocksContext.ORGANIZATION_ID_CLAIM, BlocksContext.ORGANIZATION_ID_CLAIM);

            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.ROLES_CLAIM, BlocksContext.ROLES_CLAIM);
            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.PERMISSION_CLAIM, BlocksContext.PERMISSION_CLAIM);
            AddArrayClaimIfPresent(principal, userInfo, BlocksContext.SERVICE_ACCESS_CLAIM, BlocksContext.SERVICE_ACCESS_CLAIM);

            return (true, userInfo);
        }

        private static HashSet<string> GetGrantedScopes(ClaimsPrincipal principal)
        {
            var scopeValues = principal.FindAll("scope")
                .Concat(principal.FindAll("scp"))
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return scopeValues
                .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasScope(HashSet<string> grantedScopes, string scope)
        {
            return grantedScopes.Count == 0 || grantedScopes.Contains(scope);
        }

        private static void AddSingleClaimIfPresent(ClaimsPrincipal principal, Dictionary<string, object> userInfo, string outputClaimName, string sourceClaimType)
        {
            var value = principal.FindFirst(sourceClaimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                userInfo[outputClaimName] = value;
            }
        }

        private static void AddArrayClaimIfPresent(ClaimsPrincipal principal, Dictionary<string, object> userInfo, string outputClaimName, string sourceClaimType)
        {
            var values = principal.FindAll(sourceClaimType)
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (values.Length > 0)
            {
                userInfo[outputClaimName] = values;
            }
        }

        private async Task<IActionResult> BuildTokenResponseAsync(TokenResponse response, HttpContext httpContext)
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                var statusCode = response.StatusCode > 0 ? response.StatusCode : StatusCodes.Status400BadRequest;
                return new ObjectResult(new
                {
                    error = response.Error,
                    error_description = response.ErrorDescription,
                    redirect_url = response.SsoUserRedirectUrl
                })
                {
                    StatusCode = statusCode
                };
            }

            AppendCookies(response, httpContext.Response);
            await EnsureIdpSessionForLoginAsync(response, httpContext);
            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                refresh_token = response.RefreshToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                expires_utc = response.ExpiresUtc,
                refresh_expires_utc = response.RefreshExpiresUtc,
                scope = response.Scope,
                id_token = response.IdToken
            });
        }

        private static void AppendCookies(TokenResponse response, HttpResponse httpResponse)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
            var accessCookieOptions = CreateCookieOptions(response.CookieDomain, response.ExpiresUtc);
            var refreshCookieOptions = CreateCookieOptions(response.CookieDomain, response.RefreshExpiresUtc);

            if (!string.IsNullOrWhiteSpace(response.AccessToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.AccessTokenCookieName}_{tenantId}", response.AccessToken, accessCookieOptions);
            }

            if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            {
                httpResponse.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tenantId}", response.RefreshToken, refreshCookieOptions);
            }
        }

        private static CookieOptions CreateCookieOptions(string? domain, DateTime expiresUtc)
        {
            return new CookieOptions
            {
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = expiresUtc == default ? DateTime.UtcNow.AddHours(1) : expiresUtc
            };
        }

        private async Task EnsureIdpSessionForLoginAsync(TokenResponse tokenResponse, HttpContext httpContext)
        {
            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                return;
            }

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenResponse.AccessToken);
            var userId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.USER_ID_CLAIM)?.Value;
            var tenantId = jwt.Claims.FirstOrDefault(c => c.Type == BlocksContext.TENANT_ID_CLAIM)?.Value;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            {
                return;
            }

            var sessionId = httpContext.Request.Cookies[IdpSessionCookieName];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }
            else
            {
                var existingSession = await _idpSessionService.GetSessionAsync(sessionId);
                if (existingSession == null || existingSession.RevokedAt.HasValue || existingSession.IsExpired())
                {
                    sessionId = await _idpSessionService.CreateSessionAsync(userId, tenantId, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                }
                else
                {
                    var accountExists = existingSession.Accounts.Any(a =>
                        string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

                    if (!accountExists)
                    {
                        await _idpSessionService.AddAccountAsync(sessionId, userId, tenantId, userId);
                    }
                    else
                    {
                        await _idpSessionService.UpdateActivityAsync(sessionId);
                    }
                }
            }

            httpContext.Response.Cookies.Append(IdpSessionCookieName, sessionId, CreateCookieOptions(tokenResponse.CookieDomain, tokenResponse.RefreshExpiresUtc));
        }
    }
}
