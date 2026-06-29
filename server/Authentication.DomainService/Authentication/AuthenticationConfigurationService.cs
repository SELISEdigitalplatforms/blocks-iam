using System.Linq.Expressions;
using Authentication.DomainService.Authentication.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthenticationConfigurationService : IAuthenticationConfigurationService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;

        public AuthenticationConfigurationService(IAuthenticationRepository authenticationRepository, ITenants tenants)
        {
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
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
                config?.PasswordStrengthCheckerRegex
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

            var accountActionBaseUrl =
                !string.IsNullOrWhiteSpace(configuration.AccountActionBaseUrl)
                    ? configuration.AccountActionBaseUrl
                    : current?.AccountActionBaseUrl;

            if ((!isOidcEnabled
                    && useAccountActionBaseUrlAsDefault
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
                AccountActionBaseUrl = accountActionBaseUrl
            };

            await _authenticationRepository.UpdateAuthenticationConfigurationAsync(authConfiguration);

            return new BaseResponse
            {
                IsSuccess = true
            };
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