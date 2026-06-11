using Blocks.Genesis;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using CloudConfiguration.DomainService.Authentication;
using CloudConfiguration.DomainService.Authentication.Entities;
using CloudConfiguration.DomainService.Captcha.Entities;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.MFA.RequestModel;
using CloudConfiguration.DomainService.MFA.ResponseModel;
using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.Shared.Utilities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.Enums;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private const string MaskedSecretValue = "********";

        private readonly IConfigurationRepository _configurationRepository;
        private readonly IValidator<SaveCaptchaConfigurationRequest> _captchaConfigurationValidator;
        private readonly IValidator<SaveNotificatonConfigurationRequest> _notificatonConfigurationValidator;
        private readonly IValidator<SaveStorageConfigurationRequest> _storageConfigurationValidator;
        private readonly IValidator<MailConfiguration> _mailConfigurationValidator;
        private readonly IMessageClient _messageClient;
        private readonly ITenants _tenants;
        private readonly ILogger<ConfigurationService> _logger;


        public ConfigurationService(IConfigurationRepository configurationRepository,
                                    IValidator<SaveCaptchaConfigurationRequest> configurationValidator,
                                    IValidator<SaveNotificatonConfigurationRequest> notificatonConfigurationValidator,
                                    IValidator<SaveStorageConfigurationRequest> storageConfigurationValidator,
                                    IValidator<MailConfiguration> mailConfigurationValidator,
                                    IMessageClient messageClient,
                                    ILogger<ConfigurationService> logger,
                                    ITenants tenants)
        {
            _configurationRepository = configurationRepository;
            _captchaConfigurationValidator = configurationValidator;
            _notificatonConfigurationValidator = notificatonConfigurationValidator;
            _storageConfigurationValidator = storageConfigurationValidator;
            _mailConfigurationValidator = mailConfigurationValidator;
            _messageClient = messageClient;
            _logger = logger;
            _tenants = tenants;
        }

        #region Authentication 

        public async Task<IActionResult> GetAuthenticationConfigAsync()
        {
            var config = await _configurationRepository.GetAuthenticationConfigurationAsync();
            var publicCertificatePath = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.JwtTokenParameters.PublicCertificatePath;

            return new OkObjectResult(new
            {
                ItemId = config.ItemId.ToString(),
                config.RefreshTokenValidForNumberMinutes,
                config.AbsoluteRefreshTokenValidForNumberMinutes,
                config.AccessTokenValidForNumberMinutes,
                config.RememberMeRefreshTokenValidForNumberMinutes,
                config.AllowedGrantTypes,
                config.GetNumberOfWrongAttemptsToLockTheAccount,
                config.AccountLockDurationInMinutes,
                PublicCertificatePath = publicCertificatePath,
                config.AccountActivationPath,
                config.AccountVerificationPath,
                config.RecoverAccountPath,
                config.IsOidcEnabled,
                config.AccountActionBaseUrl,
                config.UseAccountActionBaseUrlAsDefault,
                config.ActivationUrlLifetimeInMinutes,
                config.RecoverAccountUrlLifetimeInMinutes,
                config.LogoutOnPasswordChange,
                config.PasswordStrengthCheckerRegex
            });
        }

        public async Task<BaseResponse> UpdateAuthenticationConfigAsync(UpdateAuthenticationConfigurationRequest configuration)
        {
            var current = await _configurationRepository.GetAuthenticationConfigurationAsync();

            var tenant = _tenants.GetTenantByID(
                BlocksContext.GetContext()?.TenantId ?? string.Empty);

            // Resolve effective values first
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

                AllowedGrantTypes =
                    configuration.AllowedGrantTypes?.Count > 0
                        ? configuration.AllowedGrantTypes
                        : current?.AllowedGrantTypes ?? [],

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
                    60 * 24),

                RecoverAccountUrlLifetimeInMinutes = ResolveInt(
                    configuration.RecoverAccountUrlLifetimeInMinutes,
                    current?.RecoverAccountUrlLifetimeInMinutes,
                    10),

                PasswordStrengthCheckerRegex = ResolveString(
                    configuration.PasswordStrengthCheckerRegex,
                    current?.PasswordStrengthCheckerRegex),

                IsOidcEnabled = isOidcEnabled,
                UseAccountActionBaseUrlAsDefault = useAccountActionBaseUrlAsDefault,
                LogoutOnPasswordChange = logoutOnPasswordChange,
                AccountActionBaseUrl = accountActionBaseUrl
            };

            await _configurationRepository.UpdateAuthenticationConfigAsync(authConfiguration);

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

        #endregion

        #region Captcha

        public async Task<GetCaptchaConfigurationResponse> GetCaptchaConfigurationAsync(string provider)
        {
            var configuration = await _configurationRepository.GetCaptchaConfigurationByProviderAsync(provider);
            return configuration == null ? new GetCaptchaConfigurationResponse { IsSuccess = true, Errors = new Dictionary<string, string> { { "no_configuration_exist", $"No configuration exist with provider {provider}" } } } : new GetCaptchaConfigurationResponse { IsSuccess = true, Configuration = configuration };
        }

        public async Task<GetCaptchaConfigurationsResponse> GetCaptchaConfigurationsAsync(GetCaptchaConfigurationsRequest request)
        {
            return await _configurationRepository.GetCaptchaConfigurationsAsync(request);
        }

        public async Task<BaseMutationResponse> SaveCaptchaConfigurationAsync(SaveCaptchaConfigurationRequest request)
        {
            var validationResult = await _captchaConfigurationValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var repoConfiguration = await MappedIntoCaptchaRepoConfigurationAsync(request);
            await _configurationRepository.SaveCaptchaConfigurationAsync(repoConfiguration);

            return new BaseMutationResponse { IsSuccess = true };
        }

        public async Task<BaseMutationResponse> UpdateCaptchaConfigurationStatusAsync(UpdateCaptchaConfigurationStatusRequest request)
        {
            await _configurationRepository.UpdateCaptchaConfigurationStatusAsync(request);
            return new BaseMutationResponse { IsSuccess = true };
        }

        private async Task<CaptchaConfiguration> MappedIntoCaptchaRepoConfigurationAsync(SaveCaptchaConfigurationRequest configuration)
        {
            var repoConfiguration = await _configurationRepository.GetCaptchaConfigurationByProviderAsync(configuration.Provider);

            repoConfiguration ??= new CaptchaConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow };

            repoConfiguration.CreatedBy = BlocksContext.GetContext()?.TenantId;
            repoConfiguration.LastUpdatedBy = BlocksContext.GetContext()?.TenantId;
            repoConfiguration.LastUpdatedDate = DateTime.UtcNow;

            repoConfiguration.CaptchaKey = configuration.CaptchaKey;
            repoConfiguration.CaptchaSecret = configuration.CaptchaSecret;
            repoConfiguration.Provider = configuration.Provider;
            repoConfiguration.CaptchaGenerator = configuration.CaptchaGenerator;
            repoConfiguration.IsEnable = configuration.IsEnable;

            return repoConfiguration;
        }

        #endregion

        #region MFA

        public async Task<BaseResponse> SaveMfaConfigurationAsync(SaveMfaConfigurationRequest request)
        {
            var mafConfiguration = await _configurationRepository.GetDefaultMfaConfiguration();

            mafConfiguration ??= new MfaConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId ?? "" };

            mafConfiguration.EnableMfa = request.EnableMfa;
            mafConfiguration.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "";
            mafConfiguration.UserMfaTypes = request.UserMfaType;
            mafConfiguration.MfaTemplate = request.MfaTemplate ?? new MfaTemplate { TemplateId = Constants.DefaultMfaTemplateId, TemplateName = Constants.DefaultMfaTemplateName };

            await _configurationRepository.UpsertAsync(mafConfiguration, (m => m.ItemId == mafConfiguration.ItemId));

            var bc = BlocksContext.GetContext();
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<MfaActionEvent>
            {
                Payload = new MfaActionEvent
                {
                    IsEnable = mafConfiguration.EnableMfa,
                },
                ConsumerName = Constants.AuthenticationQueue,
            });

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<GetMfaConfigurationResponse> GetMfaConfigurationAsync()
        {
            var repoConfiguration = await _configurationRepository.GetDefaultMfaConfiguration();

            return repoConfiguration != null ? new GetMfaConfigurationResponse { MfaTemplate = repoConfiguration.MfaTemplate, EnableMfa = repoConfiguration.EnableMfa, UserMfaType = repoConfiguration.UserMfaTypes } : new GetMfaConfigurationResponse { MfaTemplate = new MfaTemplate(), UserMfaType = [] };
        }

        #endregion

        #region Notification

        public async Task<BaseResponse> SaveNotificationConfigurationAsync(SaveNotificatonConfigurationRequest configuration)
        {
            var validationResult = await _notificatonConfigurationValidator.ValidateAsync(configuration);

            if (!validationResult.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var repoConfig = await MapAsync(configuration);
            await _configurationRepository.SaveNotificationConfigurationAsync(repoConfig);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<GetNotificationConfigurationsResponse> GetNotificationConfigurationsAsync(GetNotificationConfigurationsRequest request)
        {
            return await _configurationRepository.GetNotificationConfigurationsAsync(request);
        }

        public async Task<NotificationConfiguration> GetNotificatoinConfigurationAsync(GetNotificationConfigurationRequest request)
        {
            return await _configurationRepository.GetNotificationConfigurationByIdAsync(request.ItemId);
        }

        public async Task<BaseResponse> DeleteNotificationConfigurationAsync(DeleteNotificatoinConfigurationRequest request)
        {
            return await _configurationRepository.DeleteNotificationConfigurationAsync(request);
        }

        private async Task<NotificationConfiguration> MapAsync(SaveNotificatonConfigurationRequest configuration)
        {
            var repoConfig = await _configurationRepository.GetNotificationConfigurationByNameAsync(configuration.Name);

            repoConfig = repoConfig ?? new NotificationConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId };

            repoConfig.LastUpdatedDate = DateTime.UtcNow;
            repoConfig.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            repoConfig.Name = configuration.Name;
            repoConfig.ChannelToNotify = configuration.ChannelToNotify;
            repoConfig.NotificationType = configuration.NotificationType;
            repoConfig.EnablePersistence = configuration.EnablePersistence;
            repoConfig.NotifyMethod = configuration.NotifyMethod;

            return repoConfig;
        }

        #endregion

        #region Storage

        public async Task<BaseMutationResponse> SaveStorageConfigurationAsync(SaveStorageConfigurationRequest request)
        {
            var validationResult = await _storageConfigurationValidator.ValidateAsync(request);

            if (!validationResult.IsValid)
                return new BaseMutationResponse { IsSuccess = false, Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };

            var repoConfiguration = await MappedIntoRepoConfigurationAsync(request);

            await _configurationRepository.SaveStorageConfigurationAsync(repoConfiguration);

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<CreateDefaultFolderEvent>
            {
                Payload = new CreateDefaultFolderEvent
                {
                    ItemId = repoConfiguration.ItemId,
                    ConfigurationName = repoConfiguration.Name,
                    StorageStrategy = repoConfiguration.StorageStrategy
                },
                ConsumerName = Constants.StorageQueue,
            });

            return new BaseMutationResponse { IsSuccess = true, ItemId = repoConfiguration.ItemId };
        }

        private async Task<StorageConfiguration> MappedIntoRepoConfigurationAsync(SaveStorageConfigurationRequest request)
        {
            var repoConfiguration = request.UpdateRequest ?
                                    await _configurationRepository.GetStorageConfigurationByIdAsync(request.ItemId ?? "") :
                                    await _configurationRepository.GetStorageConfigurationByNameAsync(request.Name);

            if (repoConfiguration == null)
            {
                repoConfiguration = new StorageConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow };
            }

            repoConfiguration.CreatedBy = BlocksContext.GetContext()?.UserId;
            repoConfiguration.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            repoConfiguration.LastUpdatedDate = DateTime.UtcNow;
            repoConfiguration.Name = request.Name;
            repoConfiguration.ConnectionString = request.ConnectionString;
            repoConfiguration.SecretKey = request.SecretKey;
            repoConfiguration.StorageStrategy = request.StorageStrategy;
            repoConfiguration.AccessKey = request.AccessKey;
            repoConfiguration.CloudStorageRegionEndPoint = request.CloudStorageRegionEndPoint;


            #region LocalStorage

            _ = StorageTypes.TryGetCategory(request.StorageStrategy, out var category);

            repoConfiguration.Host = request.Host ?? "";
            repoConfiguration.Port = request.Port ?? "";
            repoConfiguration.UserName = request.UserName ?? "";
            repoConfiguration.Password = request.Password ?? "";
            repoConfiguration.RemoteBasePath = request.RemoteBasePath ?? "";
            repoConfiguration.StorageStrategy = request.StorageStrategy ?? "";
            repoConfiguration.SftpSecretKey = category == StorageStrategyCategory.Local ? Helper.GenerateAesKey() : "";

            #endregion

            return repoConfiguration;
        }

        public static class StorageTypes
        {
            private static readonly ImmutableDictionary<string, StorageStrategyCategory> TypeToCategory =
                ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
                {
                   new KeyValuePair<string, StorageStrategyCategory>("azure", StorageStrategyCategory.Cloud),
                   new KeyValuePair<string, StorageStrategyCategory>("aws", StorageStrategyCategory.Cloud),
                   new KeyValuePair<string, StorageStrategyCategory>("sftpstorage", StorageStrategyCategory.Local)
                });

            public static bool TryGetCategory(string type, out StorageStrategyCategory category)
            {
                return TypeToCategory.TryGetValue(type.ToLower(), out category);
            }
        }

        public async Task<List<StorageConfiguration>> GetStorageConfigurationsAsync()
        {
            var configurations = await _configurationRepository.GetAllStorageConfigurationsByDateAsync();

            foreach (var configuration in configurations)
            {
                //To do masking if required
                if (configuration.StorageStrategy == "SftpStorage")
                {
                    configuration.Password = MaskedSecretValue;
                    configuration.SftpSecretKey = MaskedSecretValue;
                }
                else
                {
                    configuration.ConnectionString = Helper.GetMaskedCloudStorageRegionEndPoint(configuration.ConnectionString);
                }
            }

            return configurations;
        }

        public async Task<StorageConfiguration> GetStorageConfigurationAsync(string configurationName)
        {
            var configuration = await _configurationRepository.GetStorageConfigurationByNameAsync(configurationName);

            //To do masking if required
            if (configuration.StorageStrategy == "SftpStorage")
            {
                configuration.Password = MaskedSecretValue;
                configuration.SftpSecretKey = MaskedSecretValue;
            }
            else
            {
                configuration.ConnectionString = Helper.GetMaskedCloudStorageRegionEndPoint(configuration.ConnectionString);
            }

            return configuration;
        }

        public async Task<BaseResponse> DeleteStorageConfigurationAsync(string configurationName)
        {
            await _configurationRepository.DeleteStorageConfigurationByNameAsync(configurationName);

            return new BaseResponse { IsSuccess = true };
        }

        #endregion

        #region Mail

        public async Task<BaseMutationResponse> SaveMailConfigurationAsync(MailConfiguration configuration)
        {
            _logger.LogInformation("Saving mail configuration start");
            var validationResult = await _mailConfigurationValidator.ValidateAsync(configuration);

            if (!validationResult.IsValid)
            {
                _logger.LogInformation("Saving mail configuration end -- Validation Error");
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var repoConfiguration = await MappedIntoMailRepoConfigurationAsync(configuration);
            await _configurationRepository.SaveMailConfigurationAsync(repoConfiguration);

            _logger.LogInformation("Saving mail configuration end -- Success");
            return new BaseMutationResponse { IsSuccess = true };
        }

        public async Task<MailConfiguration> GetMailConfigurationAsync(GetMailConfigurationRequest request)
        {
            var configuration = await _configurationRepository.GetMailConfigurationByNameAsync(request.ConfigurationName);
            configuration.AccountPassword = MaskedSecretValue;
            return configuration;
        }

        public async Task<List<MailServerConfiguration>> GetAllMailConfigurationsAsync()
        {
            var configurations = await _configurationRepository.GetAllMailConfigurationsAsync();

            foreach (var config in configurations)
            {
                config.AccountPassword = MaskedSecretValue;
            }

            return configurations;
        }

        public async Task<BaseMutationResponse> DeleteMailConfigurationAsync(DeleteMailConfigurationRequest request)
        {
            _logger.LogInformation("Deleting mail configuration start");

            var config = await _configurationRepository.GetMailConfigurationByIdAsync(request.ConfigurationId);
            if (config == null)
            {
                _logger.LogInformation("Deleting mail configuration end -- Configuration not found");

                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ConfigurationId", "Configuration not found" }
                    }
                };
            }

            await _configurationRepository.DeleteMailConfigurationAsync(request.ConfigurationId);

            _logger.LogInformation("Deleting mail configuration end -- Success");
            return new BaseMutationResponse { IsSuccess = true };
        }

        public async Task<BaseMutationResponse> DuplicateMailConfigurationAsync(DuplicateMailConfigurationRequest request)
        {
            _logger.LogInformation("Duplicating mail configuration start");

            var config = await _configurationRepository.GetMailConfigurationByIdAsync(request.ConfigurationId);
            if (config == null)
            {
                _logger.LogInformation("Duplicating mail configuration end -- Configuration not found");

                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "ConfigurationId", "Configuration not found" }
                    }
                };
            }

            var newConfig = new MailServerConfiguration
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
                OrganizationIds = config.OrganizationIds,
                Tags = config.Tags,
                Name = config.Name + " - Copy",
                Host = config.Host,
                Port = config.Port,
                EnableSSL = config.EnableSSL,
                SenderName = config.SenderName,
                SenderAddress = config.SenderAddress,
                SenderUserName = config.SenderUserName,
                AccountPassword = config.AccountPassword,
                UseDefaultCredentials = config.UseDefaultCredentials,
                SmtpClient = config.SmtpClient,
                IsDefault = config.IsDefault,
                Provider = config.Provider,
                IsInbound = config.IsInbound
            };

            await _configurationRepository.SaveMailConfigurationAsync(newConfig);

            _logger.LogInformation("Duplicating mail configuration end -- Success");
            return new BaseMutationResponse { IsSuccess = true };
        }

        private async Task<MailServerConfiguration> MappedIntoMailRepoConfigurationAsync(MailConfiguration configuration)
        {
            var repoConfiguration = await _configurationRepository.GetMailConfigurationByIdAsync(configuration.ConfigurationId);

            repoConfiguration ??= new MailServerConfiguration { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow };

            repoConfiguration.LastUpdatedDate = DateTime.UtcNow;
            repoConfiguration.Host = configuration.Host;
            repoConfiguration.Port = configuration.Port;
            repoConfiguration.Name = configuration.ConfigurationName;
            repoConfiguration.SenderName = configuration.SenderName;
            repoConfiguration.SenderUserName = configuration.SenderUserName;
            repoConfiguration.SenderAddress = configuration.SenderAddress;
            repoConfiguration.AccountPassword = configuration.AccountPassword;
            repoConfiguration.EnableSSL = configuration.EnableSSL;
            repoConfiguration.CreatedBy = BlocksContext.GetContext()?.UserId ?? "";
            repoConfiguration.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "";
            repoConfiguration.IsInbound = configuration.IsInbound;
            repoConfiguration.Provider = configuration.Provider;
            return repoConfiguration;
        }

        #endregion
    }
}
