using Blocks.Genesis;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Shared;

namespace Authentication.DomainService.OAuth
{
    public sealed class OAuthJwtAccessTokenManager : IOAuthJwtAccessTokenManager
    {
        private readonly IJwtAccessTokenProvider _jwtAccessTokenProvider;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IOtpServiceFactory _otpServiceFactory;
        private readonly IMfaPolicyService _mfaPolicyService;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly UnifiedTokenSessionService _unifiedTokenSessionService;

        private static readonly HashSet<string> MfaCheckpointExemptGrantTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            GrantTypes.MfaCode,
            GrantTypes.ClientCredential,
            GrantTypes.RefreshToken,
            GrantTypes.SwitchOrganization,
            GrantTypes.ImpersonationCloud
        };

        private static bool IsMfaCheckpointExempt(string? grantType) =>
            !string.IsNullOrWhiteSpace(grantType) && MfaCheckpointExemptGrantTypes.Contains(grantType);

        public OAuthJwtAccessTokenManager(
            IJwtAccessTokenProvider jwtAccessTokenProvider,
            IAuthenticationDomainService authenticationDomainService,
            IAuthenticationRepository authenticationRepository,
            IMfaPolicyService mfaPolicyService,
            ICacheClient cacheClient,
            ITenants tenants,
            IOtpServiceFactory otpServiceFactory,
            UnifiedTokenSessionService unifiedTokenSessionService
        )
        {
            _jwtAccessTokenProvider = jwtAccessTokenProvider;
            _authenticationDomainService = authenticationDomainService;
            _authenticationRepository = authenticationRepository;
            _mfaPolicyService = mfaPolicyService;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _otpServiceFactory = otpServiceFactory;
            _unifiedTokenSessionService = unifiedTokenSessionService;
        }

        public async Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, IdentityConfiguration authenticationConfiguration, User user, StateInfo? stateInfo = null)
        {
            var bc = BlocksContext.GetContext();

            var checkpointResponse = await ProcessCheckPointsAsync(tokenRequest, user, tokenRequest.ClientId);
            if (checkpointResponse != null)
            {
                return checkpointResponse;
            }

            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "");
            if (tenant == null)
            {
                return new TokenResponse
                {
                    Error = "invalid_tenant",
                    ErrorDescription = "Tenant not found",
                    StatusCode = 400
                };
            }

            var (_, allowedServiceAccessResources) = await ResolveClientAuthorizationConfigAsync(tokenRequest.ClientId);
            var jwtAccessToken = await _jwtAccessTokenProvider.GetJwtAccessToken(
                authenticationConfiguration,
                tenant,
                user,
                tokenRequest,
                stateInfo,
                clientAllowedServiceAccessResources: allowedServiceAccessResources);

            var accessToken = CreateJwtAccessToken(jwtAccessToken);
            var (refreshToken, refreshValidity) = await ManageRefreshTokenAsync(tokenRequest, jwtAccessToken, authenticationConfiguration, tenant, user);
            var (_, cookieDomain, _) = DomainResolver.ResolveDomain(tenant, tokenRequest.Request);

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                ExpiresUtc = jwtAccessToken.Expires,
                RefreshToken = refreshToken,
                RefreshExpiresUtc = refreshValidity,
                CookieDomain = cookieDomain,
                StatusCode = 200
            };
        }

        private async Task<(IReadOnlyCollection<string> AllowedScopes, IReadOnlyCollection<string> AllowedServiceAccessResources)> ResolveClientAuthorizationConfigAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return ([], []);
            }

            var oidcClient = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            var allowedScopes = oidcClient?.AllowedScopes?
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];

            var allowedServiceAccessResources = oidcClient?.AllowedServiceAccessResources?
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? [];

            return (allowedScopes, allowedServiceAccessResources);
        }

        private async Task<TokenResponse?> ProcessCheckPointsAsync(TokenRequest tokenRequest, User user, string? clientId)
        {
            if (IsMfaCheckpointExempt(tokenRequest.GrantType))
            {
                return null;
            }

            var policy = await _mfaPolicyService.EvaluateAsync(user, clientId);
            if (!policy.Required)
            {
                return null;
            }

            if (policy.MustEnrollFirst)
            {
                return new TokenResponse
                {
                    Error = OAuthError.MfaEnrollmentRequired,
                    ErrorDescription = "Mfa enrollment is required before authentication can complete",
                    MfaRequired = true,
                    MfaMethods = string.Join(",", policy.AllowedMethods.Select(m => m.ToString())),
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            return await HandleMfaAuthenticationAsync(user, clientId, policy);
        }

        private async Task<TokenResponse> HandleMfaAuthenticationAsync(User user, string? clientId, MfaPolicyDecision policy)
        {
            try
            {
                var mfaType = policy.PreferredMethod ?? user.UserMfaType;
                var otpService = _otpServiceFactory.GetOTPService(mfaType);
                if (otpService == null)
                {
                    return new TokenResponse
                    {
                        Error = "server_error",
                        ErrorDescription = "Mfa provider is not available",
                        StatusCode = 500
                    };
                }

                var response = await otpService.GenerateAsync(new UserInfo
                {
                    Email = user.Email,
                    ItemId = user.ItemId,
                    Language = user.Language ?? "en-US"
                });

                if (response == null || string.IsNullOrWhiteSpace(response.MfaId))
                {
                    return new TokenResponse
                    {
                        Error = "server_error",
                        ErrorDescription = "Failed to generate mfa challenge",
                        StatusCode = 500
                    };
                }

                return new TokenResponse
                {
                    MfaId = response.MfaId,
                    UserMfa = mfaType,
                    MfaRequired = true,
                    MfaMethods = string.Join(",", policy.AllowedMethods.Select(m => m.ToString())),
                    Error = OAuthError.MfaEnabled,
                    ErrorDescription = "Mfa code required",
                    ClientId = clientId,
                    StatusCode = 200
                };
            }
            catch
            {
                return new TokenResponse
                {
                    Error = "server_error",
                    ErrorDescription = "Unable to initiate mfa challenge",
                    StatusCode = 500
                };
            }
        }

        public static string CreateJwtAccessToken(JwtAccessToken jwtAccessToken, StateInfo? stateInfo = null)
        {
            

            var jwtToken = new JwtSecurityToken(
                jwtAccessToken.Issuer,
                jwtAccessToken.Audience,
                jwtAccessToken.Claims,
                jwtAccessToken.NotBefore,
                jwtAccessToken.Expires,
                jwtAccessToken.SigningCredentials);

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }

        public async Task<(string, DateTime)> ManageRefreshTokenAsync(TokenRequest tokenRequest, JwtAccessToken jwtAccessToken, IdentityConfiguration authenticationConfiguration, Tenant tenant, User user)
        {
            var visitorsIpAddresses = _authenticationDomainService.GetVisitorsIpAddresses(tokenRequest.Request.HttpContext) ?? new List<string>();
            // Unify both initial and rotation flows
            if (tokenRequest.GrantType == GrantTypes.RefreshToken || tokenRequest.GrantType == GrantTypes.SwitchOrganization)
            {
                // Rotation: fetch old token from cache
                var oldRefreshTokenCacheStr = await _cacheClient.GetStringValueAsync(tokenRequest.RefreshToken);
                RefreshTokenCache? oldRefreshTokenCache = null;
                if (!string.IsNullOrWhiteSpace(oldRefreshTokenCacheStr))
                {
                    oldRefreshTokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(oldRefreshTokenCacheStr);
                }
                return await _unifiedTokenSessionService.CreateOrRotateRefreshToken(
                    tokenRequest.RefreshToken,
                    oldRefreshTokenCache,
                    tokenRequest,
                    authenticationConfiguration,
                    tenant,
                    user,
                    visitorsIpAddresses,
                    tokenRequest.IsImpersonation
                );
            }
            else
            {
                // Initial auth flow: no old token
                return await _unifiedTokenSessionService.CreateOrRotateRefreshToken(
                    null,
                    null,
                    tokenRequest,
                    authenticationConfiguration,
                    tenant,
                    user,
                    visitorsIpAddresses,
                    tokenRequest.IsImpersonation
                );
            }
        }

    }
}