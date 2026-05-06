using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Authentication.DomainService.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ILogger<AuthenticationService> _logger;
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ITenants _tenants;

        private const string Public_Cert_Cache_Prefix = "tetocertpublic::";

        public AuthenticationService(
            ILogger<AuthenticationService> logger,
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService,
            ITenants tenants
        )
        {
            _logger = logger;
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
            _tenants = tenants;
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
    }
}
