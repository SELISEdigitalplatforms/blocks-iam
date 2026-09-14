using System.Linq.Expressions;
using Authentication.DomainService.Authentication.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthenticationConfigurationService : IAuthenticationConfigurationService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IConfiguration? _configuration;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly ILogger<AuthenticationConfigurationService>? _logger;

        public AuthenticationConfigurationService(
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            IConfiguration? configuration = null,
            IHttpContextAccessor? httpContextAccessor = null,
            ILogger<AuthenticationConfigurationService>? logger = null)
        {
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<IActionResult> GetAuthenticationConfigAsync()
        {
            var config = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var publicCertificatePath = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.JwtTokenParameters.PublicCertificatePath;

            return new OkObjectResult(new
            {
                ItemId = config?.ItemId.ToString(),
                config?.RefreshTokenValidForNumberMinutes,
                config?.AbsoluteRefreshTokenValidForNumberMinutes,
                config?.AccessTokenValidForNumberMinutes,
                config?.RememberMeRefreshTokenValidForNumberMinutes,
                config?.AllowedGrantTypes,
                config?.GetNumberOfWrongAttemptsToLockTheAccount,
                config?.AccountLockDurationInMinutes,
                PublicCertificatePath = publicCertificatePath,
                config?.AccountActivationPath,
                config?.AccountVerificationPath,
                config?.RecoverAccountPath,
                config?.IsOidcEnabled,
                config?.AccountActionBaseUrl,
                config?.UseAccountActionBaseUrlAsDefault,
                config?.ActivationUrlLifetimeInMinutes,
                config?.RecoverAccountUrlLifetimeInMinutes,
                config?.LogoutOnPasswordChange,
                config?.PasswordStrengthCheckerRegex,
                config?.CollectPasswordOnActivation
            });
        }

        public async Task<BaseResponse> UpdateAuthenticationConfigAsync(UpdateAuthenticationConfigurationRequest configuration)
        {
            var current = await _authenticationRepository.GetAuthenticationConfigurationAsync();

            var tenant = _tenants.GetTenantByID(
                BlocksContext.GetContext()?.TenantId ?? string.Empty);

            var isOidcEnabled =
                configuration.IsOidcEnabled
                ?? current?.IsOidcEnabled
                ?? false;

            var useAccountActionBaseUrlAsDefault =
                configuration.UseAccountActionBaseUrlAsDefault
                ?? current?.UseAccountActionBaseUrlAsDefault
                ?? true;

            var logoutOnPasswordChange =
                configuration.LogoutOnPasswordChange
                ?? current?.LogoutOnPasswordChange
                ?? true;

            var collectPasswordOnActivation =
                configuration.CollectPasswordOnActivation
                ?? current?.CollectPasswordOnActivation
                ?? IdentityConfiguration.DefaultCollectPasswordOnActivation;

            string? accountActionBaseUrl;

            if (isOidcEnabled)
            {
                // Under OIDC, activation and recovery land on IAM's own hosted pages, so the
                // base URL is a property of this deployment (dev / stg / prod), not something
                // the caller gets to choose. Take it from BLOCKS_IAM_BASE_URL and ignore the
                // payload; there is nothing tenant-scoped left to validate against.
                accountActionBaseUrl = ResolveIamBaseUrl();

                if (string.IsNullOrWhiteSpace(accountActionBaseUrl))
                {
                    // Neither configured nor derivable from the request - leave whatever is
                    // stored rather than clearing a working value.
                    _logger?.LogWarning(
                        "OIDC is enabled but BLOCKS_IAM_BASE_URL could not be resolved. "
                        + "Keeping the stored AccountActionBaseUrl.");

                    accountActionBaseUrl = current?.AccountActionBaseUrl;
                }
            }
            else
            {
                accountActionBaseUrl =
                    !string.IsNullOrWhiteSpace(configuration.AccountActionBaseUrl)
                        ? configuration.AccountActionBaseUrl
                        : current?.AccountActionBaseUrl;

                if ((useAccountActionBaseUrlAsDefault
                        && string.IsNullOrWhiteSpace(accountActionBaseUrl))
                    ||
                    (!string.IsNullOrWhiteSpace(accountActionBaseUrl)
                        && !IsAllowedAccountActionBaseUrl(accountActionBaseUrl, tenant)))
                {
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            {
                                "AccountActionBaseUrl",
                                "AccountActionBaseUrl_Must_Be_In_Tenant_Allowed_Domains"
                            }
                        }
                    };
                }
            }

            static int ResolveInt(int requested, int? currentValue, int defaultValue)
                => requested > 0
                    ? requested
                    : currentValue ?? defaultValue;

            static string ResolveString(string requested, string currentValue)
                => !string.IsNullOrWhiteSpace(requested)
                    ? requested
                    : currentValue;

            var authConfiguration = new IdentityConfiguration
            {
                ItemId = current?.ItemId ?? ObjectId.Parse(configuration.ItemId),

                RefreshTokenValidForNumberMinutes = ResolveInt(
                    configuration.RefreshTokenValidForNumberMinutes,
                    current?.RefreshTokenValidForNumberMinutes,
                    IdentityConfiguration.DefaultRefreshTokenValidForNumberMinutes),

                AbsoluteRefreshTokenValidForNumberMinutes = ResolveInt(
                    configuration.AbsoluteRefreshTokenValidForNumberMinutes,
                    current?.AbsoluteRefreshTokenValidForNumberMinutes,
                    IdentityConfiguration.DefaultAbsoluteRefreshTokenValidForNumberMinutes),

                AccessTokenValidForNumberMinutes = ResolveInt(
                    configuration.AccessTokenValidForNumberMinutes,
                    current?.AccessTokenValidForNumberMinutes,
                    IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes),

                RememberMeRefreshTokenValidForNumberMinutes = ResolveInt(
                    configuration.RememberMeRefreshTokenValidForNumberMinutes,
                    current?.RememberMeRefreshTokenValidForNumberMinutes,
                    IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes),

                GetNumberOfWrongAttemptsToLockTheAccount = ResolveInt(
                    configuration.GetNumberOfWrongAttemptsToLockTheAccount,
                    current?.GetNumberOfWrongAttemptsToLockTheAccount,
                    IdentityConfiguration.DefaultGetNumberOfWrongAttemptsToLockTheAccount),

                AccountLockDurationInMinutes = ResolveInt(
                    configuration.AccountLockDurationInMinutes,
                    current?.AccountLockDurationInMinutes,
                    IdentityConfiguration.DefaultAccountLockDurationInMinutes),

                PublicCertificatePath = ResolveString(
                    configuration.PublicCertificatePath,
                    current?.PublicCertificatePath),

                AccountActivationPath = ResolveString(
                    configuration.AccountActivationPath,
                    current?.AccountActivationPath),

                AccountVerificationPath = ResolveString(
                    configuration.AccountVerificationPath,
                    current?.AccountVerificationPath),

                RecoverAccountPath = ResolveString(
                    configuration.RecoverAccountPath,
                    current?.RecoverAccountPath),

                ActivationUrlLifetimeInMinutes = ResolveInt(
                    configuration.ActivationUrlLifetimeInMinutes,
                    current?.ActivationUrlLifetimeInMinutes,
                    IdentityConfiguration.DefaultActivationUrlLifetimeInMinutes),

                RecoverAccountUrlLifetimeInMinutes = ResolveInt(
                    configuration.RecoverAccountUrlLifetimeInMinutes,
                    current?.RecoverAccountUrlLifetimeInMinutes,
                    IdentityConfiguration.DefaultRecoverAccountUrlLifetimeInMinutes),

                PasswordStrengthCheckerRegex = ResolveString(
                    configuration.PasswordStrengthCheckerRegex,
                    current?.PasswordStrengthCheckerRegex),

                IsOidcEnabled = isOidcEnabled,
                UseAccountActionBaseUrlAsDefault = useAccountActionBaseUrlAsDefault,
                LogoutOnPasswordChange = logoutOnPasswordChange,
                CollectPasswordOnActivation = collectPasswordOnActivation,
                AccountActionBaseUrl = accountActionBaseUrl
            };

            await _authenticationRepository.UpdateAuthenticationConfigurationAsync(authConfiguration);

            return new BaseResponse
            {
                IsSuccess = true
            };
        }

        /// <summary>
        /// This IAM deployment's own base URL: the environment's configured
        /// <c>BLOCKS_IAM_BASE_URL</c>, falling back to the host the request arrived on
        /// (the config endpoint is served by IAM itself, so that host is IAM's).
        /// </summary>
        private string ResolveIamBaseUrl()
        {
            var configured = IamHelper.GetConfiguredIamBaseUrl(_configuration);

            return !string.IsNullOrWhiteSpace(configured)
                ? configured
                : IamHelper.GetOidcRequestBaseUrl(_httpContextAccessor);
        }

        private static bool IsAllowedAccountActionBaseUrl(string accountActionBaseUrl, dynamic? tenant)
        {
            if (!Uri.TryCreate(accountActionBaseUrl, UriKind.Absolute, out var targetUri))
            {
                return false;
            }

            var allowedDomains = ((IEnumerable<dynamic>?)tenant?.Applications ?? [])
                .Select(application => (string?)application?.Domain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => NormalizeHost(domain!))
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowedDomains.Count == 0)
            {
                return true;
            }

            return allowedDomains.Contains(targetUri.Host);
        }

        private static string NormalizeHost(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            return value.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .TrimEnd('/');
        }
    }
}