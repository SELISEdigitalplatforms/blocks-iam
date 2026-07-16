using Blocks.Genesis;
using Authentication.DomainService.Utilities;
using DeviceDetectorNET;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.ResponseModel;
using Authentication.DomainService.Shared;
using Iam.DomainService.Utilities;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Text.Json;
using Authentication.DomainService.Shared.ResponseModel;
using FluentValidation;
using Authentication.DomainService.Shared.RequestModel;
using Iam.DomainService.Dtos;


namespace Authentication.DomainService.Services
{
    public sealed class AuthenticationDomainService : IAuthenticationDomainService
    {
        private readonly IMessageClient _messageClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IConfiguration _configuration;
        private readonly IUserRepository _userRepository;
        private readonly IValidator<SaveSsoCredentialRequest> _validator;
        private readonly IValidator<SaveOIDCClientRequest> _oidcClientValidator;
        private readonly IValidator<SaveIdentityProviderRequest> _saveIdpValidator;
        private readonly IValidator<UpdateIdentityProviderRequest> _updateIdpValidator;
        private readonly ITenants _tenants;
        private readonly IHttpClientFactory _httpClientFactory;

        private const string Origin_Header_Name = "Origin";
        private const string Referer_Header_Name = "Referer";
        private const string X_Forwarded_For_Header_Name = "X-Forwarded-For";
        private const string DiscoveryClientName = "oidc-discovery";

        public AuthenticationDomainService(IMessageClient messageClient,
                                           IAuthenticationRepository authenticationRepository,
                                           IConfiguration configuration,
                                           IUserRepository userRepository,
                                           IValidator<SaveSsoCredentialRequest> validator,
                                           IValidator<SaveOIDCClientRequest> oidcClientValidator,
                                           IValidator<SaveIdentityProviderRequest> saveIdpValidator,
                                           IValidator<UpdateIdentityProviderRequest> updateIdpValidator,
                                           ITenants tenants,
                                           IHttpClientFactory httpClientFactory)
        {
            _messageClient = messageClient;
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _userRepository = userRepository;
            _validator = validator;
            _oidcClientValidator = oidcClientValidator;
            _saveIdpValidator = saveIdpValidator;
            _updateIdpValidator = updateIdpValidator;
            _tenants = tenants;
            _httpClientFactory = httpClientFactory;
        }

        public IEnumerable<string> GetVisitorsIpAddresses(HttpContext context)
        {
            var forwardedForHeader = context.Request.Headers[X_Forwarded_For_Header_Name];

            var visitorsIpAddress = string.IsNullOrWhiteSpace(forwardedForHeader) ? context.Connection.RemoteIpAddress?.ToString() ?? string.Empty : forwardedForHeader.ToString();

            var visitorsIpAddresses =
                visitorsIpAddress
               .Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(ipAddress => ipAddress.Trim());

            return visitorsIpAddresses;
        }

        public string GetRequestOriginHostName(HttpContext context)
        {
            var originHeaderValue = context.Request.Headers[Origin_Header_Name];

            if (!string.IsNullOrWhiteSpace(originHeaderValue))
            {
                if (Uri.TryCreate(originHeaderValue.ToString(), UriKind.Absolute, out var originUri))
                    return originUri.Host;
            }

            var refererHeaderValue = context.Request.Headers[Referer_Header_Name];

            if (!string.IsNullOrWhiteSpace(refererHeaderValue))
            {
                if (Uri.TryCreate(refererHeaderValue.ToString(), UriKind.Absolute, out var refererUri))
                    return refererUri.Host;
            }

            return string.Empty;
        }

        public async Task SendToQueueAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public async Task SendToTopicAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToMassConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public DeviceInformation? GetDeviceInfo(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return null;

            // Initialize the DeviceDetector with the User-Agent string
            var deviceDetector = new DeviceDetector(userAgent);
            deviceDetector.Parse();

            // Retrieve device details
            var clientInfo = deviceDetector.GetClient();
            var osInfo = deviceDetector.GetOs();

            return new DeviceInformation
            {
                Browser = clientInfo?.Match?.Name ?? string.Empty,
                OS = osInfo?.Match?.Name ?? string.Empty,
                Device = deviceDetector.GetDeviceName(),
                Brand = deviceDetector.GetBrandName(),
                Model = deviceDetector.GetModel()
            };
        }

        public async Task<OpenIdConnectConfiguration?> GetMetadataAsync(string wellKnownUrl)
        {
            var httpClient = _httpClientFactory.CreateClient(DiscoveryClientName);
            var response = await httpClient.GetAsync(wellKnownUrl);
            string json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<OpenIdConnectConfiguration>(json);
        }

        public async Task<SaveOIDCClientResponse> SaveOIDCClientAsync(SaveOIDCClientRequest request)
        {
            var validation = await _oidcClientValidator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                return new SaveOIDCClientResponse
                {
                    IsSuccess = false,
                    Errors = validation.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.First().ErrorMessage)
                };
            }

            var credential = await InitializeOidcClientCredentialAsync(request);
            ApplyOidcClientScopesAndRedirects(credential, request);
            ApplyOidcClientOptions(credential, request);

            await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);

            // Sync the linked Blocks IdentityProvider with the OIDC client based on the
            // RegisterAsIdentityProvider flag. Handles all three transitions:
            //   false -> true  : register a new provider
            //   true  -> true  : update the existing provider
            //   true  -> false : unregister (remove) the linked provider
            {
                var existingProvider = !string.IsNullOrWhiteSpace(credential.ClientId)
                    ? await _authenticationRepository.GetIdentityProviderByClientIdAsync(credential.ClientId)
                    : null;

                if (request.RegisterAsIdentityProvider)
                {
                    var providerName = !string.IsNullOrWhiteSpace(request.ClientDisplayName)
                        ? request.ClientDisplayName.ToLower().Replace(" ", "-")
                        : credential.ClientId.ToLower();

                    if (existingProvider == null)
                    {
                        var newProvider = new IdentityProvider
                        {
                            Provider = providerName,
                            ProviderType = IdpConstants.BlocksOidcProviderType,
                            Protocol = IdpConstants.OidcProtocol,
                            DisplayName = request.ClientDisplayName ?? credential.ClientId,
                            IsActive = credential.IsActive,
                            ClientId = credential.ClientId,
                            ClientSecret = credential.ClientSecret,
                            Issuer = credential.ExternalDiscoveryEndpoint,
                            WellKnownUrl = credential.ExternalDiscoveryEndpoint,
                            AuthorizationUrl = credential.ExternalDiscoveryEndpoint ?? "",
                            TokenUrl = credential.ExternalDiscoveryEndpoint ?? "",
                            UserInfoUrl = credential.ExternalDiscoveryEndpoint ?? "",
                            RedirectUris = credential.RedirectUris ?? new List<string>(),
                            Scope = credential.Scope,
                            ResponseType = "code",
                            GrantTypes = ["authorization_code", "refresh_token"],
                            RequirePkce = credential.RequirePkce,
                            TokenEndpointAuthMethod = credential.TokenEndpointAuthMethod,
                            InitialRoles = [],
                            InitialPermissions = [],
                            Icon = null
                        };
                        await _authenticationRepository.CreateIdentityProviderAsync(newProvider);
                    }
                    else
                    {
                        existingProvider.Provider = providerName;
                        existingProvider.ProviderType = IdpConstants.BlocksProviderType;
                        existingProvider.Protocol = IdpConstants.OidcProtocol;
                        existingProvider.ClientId = credential.ClientId;
                        existingProvider.ClientSecret = credential.ClientSecret;
                        existingProvider.DisplayName = request.ClientDisplayName ?? credential.ClientId;
                        existingProvider.Issuer = credential.ExternalDiscoveryEndpoint;
                        existingProvider.WellKnownUrl = credential.ExternalDiscoveryEndpoint;
                        existingProvider.AuthorizationUrl = credential.ExternalDiscoveryEndpoint ?? "";
                        existingProvider.TokenUrl = credential.ExternalDiscoveryEndpoint ?? "";
                        existingProvider.UserInfoUrl = credential.ExternalDiscoveryEndpoint ?? "";
                        existingProvider.RedirectUris = credential.RedirectUris ?? new List<string>();
                        existingProvider.Scope = credential.Scope;
                        existingProvider.GrantTypes = ["authorization_code", "refresh_token"];
                        existingProvider.RequirePkce = credential.RequirePkce;
                        existingProvider.TokenEndpointAuthMethod = credential.TokenEndpointAuthMethod;
                        existingProvider.IsActive = credential.IsActive;
                        await _authenticationRepository.UpdateIdentityProviderAsync(existingProvider);
                    }
                }
                else if (existingProvider != null && !string.IsNullOrWhiteSpace(existingProvider.ItemId))
                {
                    // Flag was turned off — remove the previously linked Blocks provider.
                    // Mirrors DeleteOidcClientAsync which also cleans up by ClientId.
                    await _authenticationRepository.DeleteIdentityProviderAsync(existingProvider.ItemId);
                }
            }

            return new SaveOIDCClientResponse { IsSuccess = true, ItemId = credential.ItemId };
        }

        private async Task<OidcClientRegistration> InitializeOidcClientCredentialAsync(SaveOIDCClientRequest request)
        {
            var credential = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ItemId ?? "");
            var clientId = string.IsNullOrWhiteSpace(request.ItemId) ? Guid.NewGuid().ToString() : request.ItemId;

            credential ??= new OidcClientRegistration
            {
                ItemId = clientId,
                ClientId = clientId,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
            };

            credential.ItemId = clientId;
            credential.ClientId = clientId;
            // ClientSecret rule: BE-generated on create only; preserved (never overwritten) on update.
            credential.ClientSecret = string.IsNullOrWhiteSpace(credential.ClientSecret)
                ? GenerateClientSecret()
                : credential.ClientSecret;

            return credential;
        }

        private static void ApplyOidcClientScopesAndRedirects(OidcClientRegistration credential, SaveOIDCClientRequest request)
        {
            var allowedScopes = request.AllowedScopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allowedScopes.Count == 0 && !string.IsNullOrWhiteSpace(request.Scope))
            {
                allowedScopes = request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            var redirectUris = request.RedirectUris.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            credential.AllowedScopes = allowedScopes;
            credential.Scope = string.Join(' ', allowedScopes);

            credential.RedirectUris = redirectUris;
           // credential.RedirectUri = redirectUris.FirstOrDefault();

            credential.AllowedResponseTypes = request.AllowedResponseTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (credential.AllowedResponseTypes.Count == 0)
            {
                credential.AllowedResponseTypes = ["code"];
            }
        }

        private static void ApplyOidcClientOptions(OidcClientRegistration credential, SaveOIDCClientRequest request)
        {
            credential.PostLogoutRedirectUris = request.PostLogoutRedirectUris.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            credential.ExternalDiscoveryEndpoint = request.ExternalDiscoveryEndpoint;
            credential.LoginMode = request.LoginMode;
            credential.ClientType = request.ClientType;
            credential.TokenEndpointAuthMethod = string.Equals(request.ClientType, "public", StringComparison.OrdinalIgnoreCase)
                ? "none"
                : "client_secret_post";
            credential.RequirePkce = request.RequirePkce;
            credential.RequireConsent = request.RequireConsent;
            credential.FrontChannelLogoutUri = request.FrontChannelLogoutUri;
            credential.BackChannelLogoutUri = request.BackChannelLogoutUri;
            credential.IsActive = request.IsActive;
            credential.IsAutoRedirect = request.IsAutoRedirect;
            credential.UseTokensCookie = request.UseTokensCookie;
            credential.RequireMfa = request.RequireMfa;
            credential.AllowedMfaMethods = request.AllowedMfaMethods;
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;
            credential.LogoUri = request.ClientLogoUrl;
            credential.ClientName = request.ClientDisplayName;
            credential.UiBrandColor = request.ClientBrandColor;
            credential.IsDeviceFlowClient = request.IsDeviceFlowClient;
        }

        public async Task<GetOIDCClientResponse> GetOidcClientAsync(string tenantId)
        {
            var client = await _authenticationRepository.GetOIDCCredentialByIdAsync(tenantId);
            var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(client.ClientId);
            var registerAsIdentityProvider = false;

            if (identityProvider is not null && identityProvider.ProviderType == IdpConstants.BlocksOidcProviderType)
            {
                registerAsIdentityProvider = true;
            }
            return new GetOIDCClientResponse
            {
                oIDCClientCredential = client,
                IsSuccess = true,
                registerAsIdentityProvider= registerAsIdentityProvider
            };
        }

        public async Task<GetOIDCClientsResponse> GetOidcClientsAsync()
        {
            var clients = await _authenticationRepository.GetOIDCCredentialsByTenantAsync();

            return new GetOIDCClientsResponse
            {
                oIDCClientCredentials = clients ?? [],
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> DeleteOidcClientAsync(DeleteOIDCClientRequest request)
        {
            // Get the OIDC client before deletion to know the ClientId
            var credential = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ItemId ?? "");
            
            // Delete the OIDC client registration
            await _authenticationRepository.DeleteOidcClientAsync(request);

            // Delete the related IdentityProvider by ClientId
            if (credential != null && !string.IsNullOrWhiteSpace(credential.ClientId))
            {
                var allProviders = await _authenticationRepository.GetIdentityProvidersAsync();
                var relatedProvider = allProviders?.FirstOrDefault(p => p.ClientId == credential.ClientId);
                
                if (relatedProvider != null && !string.IsNullOrWhiteSpace(relatedProvider.ItemId))
                {
                    await _authenticationRepository.DeleteIdentityProviderAsync(relatedProvider.ItemId);
                }
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> GenerateUserCodeByClientAsync(GenerateUserCodeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "invalid_request", "ClientId is required." } } };
            }

            var userCode = Guid.NewGuid().ToString("n");

            var clientUserCode = new UserCode
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientId = request.ClientId,
                UserId = BlocksContext.GetContext()?.UserId,
                Code = userCode,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
                CodeTtlInMinute = request.CodeTtlInMinute,
                Note = request.Note
            };

            await _authenticationRepository.SaveUserCodeByClientAsync(clientUserCode);
            return new BaseResponse { IsSuccess = true, };
        }

        public async Task<BaseResponse> SaveClientCredentialAsync(SaveClientCredentialRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "invalid_request", "Name is required." } } };

            var roles = (request.Roles ?? new List<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var permissions = (request.Permissions ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (roles.Count == 0 && permissions.Count == 0)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "invalid_request", "At least one role or permission is required." } } };

            var blocksContext = BlocksContext.GetContext();

            var isUpdate = !string.IsNullOrWhiteSpace(request.ItemId);
            if (isUpdate)
            {
                var existing = await _authenticationRepository.GetClientCredentialByIdAsync(request.ItemId!);
                if (existing == null)
                    return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", $"Client credential '{request.ItemId}' not found." } } };

                existing.Name = request.Name;
                existing.IsActive = request.IsActive;
                existing.AccessTokenValidForNumberMinutes = request.AccessTokenValidForNumberMinutes;
                existing.LastUpdatedBy = blocksContext?.UserId;
                existing.LastUpdatedDate = DateTime.UtcNow;
                existing.Roles = request.Roles;
                existing.Permissions = request.Permissions;

                return await _authenticationRepository.SaveClientCredentialAsync(existing);
            }

            var organizationId = blocksContext?.OrganizationId ?? "default";

            var clientCredential = new ClientCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientSecret = ClientSecretGenerator.Generate(),
                Name = request.Name,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedBy = blocksContext?.UserId,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                OrganizationId = organizationId,
                AccessTokenValidForNumberMinutes = request.AccessTokenValidForNumberMinutes,
                Roles = roles,
                Permissions = permissions,
                IsActive = true
            };

            return await _authenticationRepository.SaveClientCredentialAsync(clientCredential);
        }

        private static string GenerateClientSecret()
        {
            var secretBytes = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(secretBytes);
            return "blxsk_" + Convert.ToBase64String(secretBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public async Task<BaseResponse> DeleteClientCredentialAsync(DeleteClientCredentialRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ItemId))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "invalid_request", "ItemId is required." } } };

            var existing = await _authenticationRepository.GetClientCredentialByIdAsync(request.ItemId);
            if (existing == null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", $"Client credential '{request.ItemId}' not found." } } };

            await _authenticationRepository.DeleteClientCredentialAsync(request);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request)
        {
            return await _authenticationRepository.GetClientCredentialsAsync();
        }

        public async Task<BaseResponse> CreateIdentityProviderAsync(SaveIdentityProviderRequest request)
        {
            var validation = await _saveIdpValidator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validation.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.First().ErrorMessage)
                };
            }

            var providerName = request.Provider!;
            var clientId = request.ClientId!;

            var existingProvider = await _authenticationRepository.GetIdentityProviderAsync(providerName);
            if (existingProvider != null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "duplicate_provider", $"Provider '{providerName}' already exists." } } };

            var existingByClientId = await _authenticationRepository.GetIdentityProviderByClientIdAsync(clientId);
            if (existingByClientId != null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "duplicate_client_id", $"An identity provider with ClientId '{clientId}' already exists." } } };

            var blocksContext = BlocksContext.GetContext();
            var now = DateTime.UtcNow;
            var grantTypes = (request.GrantTypes is { Count: > 0 } ? request.GrantTypes : new List<string> { "authorization_code", "refresh_token" })
                .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var tokenEndpointAuthMethod = !string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)
                ? request.TokenEndpointAuthMethod
                : "client_secret_post";

            var provider = new IdentityProvider
            {
                ItemId = Guid.NewGuid().ToString(),
                Provider = providerName,
                ProviderType = request.ProviderType!,
                Protocol = string.IsNullOrWhiteSpace(request.Protocol) ? "oidc" : request.Protocol,
                DisplayName = request.DisplayName,
                IsActive = request.IsActive ?? true,
                ClientId = clientId,
                ClientSecret = GenerateClientSecret(),
                Issuer = request.Issuer,
                AuthorizationUrl = request.AuthorizationUrl,
                TokenUrl = request.TokenUrl,
                UserInfoUrl = request.UserInfoUrl,
                JwksUri = request.JwksUri,
                WellKnownUrl = request.WellKnownUrl,
                RedirectUris = request.RedirectUris ?? new List<string>(),
                Scope = request.Scope,
                ResponseType = request.ResponseType,
                GrantTypes = grantTypes,
                RequirePkce = request.RequirePkce ?? true,
                TokenEndpointAuthMethod = tokenEndpointAuthMethod,
                InitialRoles = request.InitialRoles ?? new List<string>(),
                InitialPermissions = request.InitialPermissions ?? new List<string>(),
                Icon = request.Icon,
                TeamId = request.TeamId,
                KeyId = request.KeyId,
                PrivateKey = request.PrivateKey,
                AppleAudience = request.AppleAudience,
                CreatedBy = blocksContext?.UserId,
                CreatedDate = now,
                LastUpdatedBy = blocksContext?.UserId,
                LastUpdatedDate = now,
            };

            await _authenticationRepository.CreateIdentityProviderAsync(provider);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<IdentityProvider?> GetIdentityProviderAsync(string provider)
        {
            return await _authenticationRepository.GetIdentityProviderAsync(provider);
        }

        public async Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id)
        {
            return await _authenticationRepository.GetIdentityProviderByIdAsync(id);
        }

        public async Task<List<IdentityProvider>> GetAllIdentityProvidersAsync()
        {
            return await _authenticationRepository.GetIdentityProvidersAsync();
        }

        public async Task<BaseResponse> UpdateIdentityProviderAsync(string id, UpdateIdentityProviderRequest request)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "id_required", "Provider ID is required." } } };

            var validation = await _updateIdpValidator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = validation.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.First().ErrorMessage)
                };
            }

            var existing = await _authenticationRepository.GetIdentityProviderByIdAsync(id);
            if (existing == null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", "Provider not found." } } };

            // Immutability check: Provider, ProviderType, Protocol, ClientId cannot change after create.
            if (request.Provider != null && !string.Equals(request.Provider, existing.Provider, StringComparison.OrdinalIgnoreCase))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "Provider is immutable and must match the existing value." } } };
            if (request.ProviderType != null && !string.Equals(request.ProviderType, existing.ProviderType, StringComparison.OrdinalIgnoreCase))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "ProviderType is immutable and must match the existing value." } } };
            if (request.Protocol != null && !string.Equals(request.Protocol, existing.Protocol, StringComparison.OrdinalIgnoreCase))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "Protocol is immutable and must match the existing value." } } };
            if (request.ClientId != null && !string.Equals(request.ClientId, existing.ClientId, StringComparison.Ordinal))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "ClientId is immutable and must match the existing value." } } };

            // Discovery re-validation when WellKnownUrl changes.
            if (request.WellKnownUrl != null && !string.Equals(request.WellKnownUrl, existing.WellKnownUrl, StringComparison.OrdinalIgnoreCase))
            {
                var probe = new SaveIdentityProviderRequest { WellKnownUrl = request.WellKnownUrl };
                var wellKnownValidation = await _saveIdpValidator.ValidateAsync(probe);
                if (!wellKnownValidation.IsValid)
                {
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = wellKnownValidation.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.First().ErrorMessage)
                    };
                }
            }

            // Partial merge: only copy non-null request fields.
            if (request.DisplayName != null) existing.DisplayName = request.DisplayName;
            if (request.IsActive.HasValue) existing.IsActive = request.IsActive.Value;
            if (request.Issuer != null) existing.Issuer = request.Issuer;
            if (request.AuthorizationUrl != null) existing.AuthorizationUrl = request.AuthorizationUrl;
            if (request.TokenUrl != null) existing.TokenUrl = request.TokenUrl;
            if (request.UserInfoUrl != null) existing.UserInfoUrl = request.UserInfoUrl;
            if (request.JwksUri != null) existing.JwksUri = request.JwksUri;
            if (request.WellKnownUrl != null) existing.WellKnownUrl = request.WellKnownUrl;
            if (request.RedirectUris != null) existing.RedirectUris = request.RedirectUris;
            if (request.Scope != null) existing.Scope = request.Scope;
            if (request.ResponseType != null) existing.ResponseType = request.ResponseType;
            if (request.GrantTypes != null) existing.GrantTypes = request.GrantTypes;
            if (request.RequirePkce.HasValue) existing.RequirePkce = request.RequirePkce.Value;
            if (request.TokenEndpointAuthMethod != null) existing.TokenEndpointAuthMethod = request.TokenEndpointAuthMethod;
            if (request.InitialRoles != null) existing.InitialRoles = request.InitialRoles;
            if (request.InitialPermissions != null) existing.InitialPermissions = request.InitialPermissions;
            if (request.Icon != null) existing.Icon = request.Icon;
            if (request.TeamId != null) existing.TeamId = request.TeamId;
            if (request.KeyId != null) existing.KeyId = request.KeyId;
            if (request.PrivateKey != null) existing.PrivateKey = request.PrivateKey;
            if (request.AppleAudience != null) existing.AppleAudience = request.AppleAudience;

            var blocksContext = BlocksContext.GetContext();
            existing.LastUpdatedBy = blocksContext?.UserId;
            existing.LastUpdatedDate = DateTime.UtcNow;

            await _authenticationRepository.UpdateIdentityProviderAsync(existing);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> DeleteIdentityProviderAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "id_required", "Provider ID is required." } } };

            var existing = await _authenticationRepository.GetIdentityProviderByIdAsync(id);
            if (existing == null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", "Provider not found." } } };

            // Delete the IdentityProvider
            await _authenticationRepository.DeleteIdentityProviderAsync(id);

            // Delete related OidcClientRegistration by ClientId
            if (!string.IsNullOrWhiteSpace(existing.ClientId))
            {
                var relatedCredential = await _authenticationRepository.GetOidcClientRegistrationAsync(existing.ClientId);
                if (relatedCredential != null)
                {
                    var deleteRequest = new DeleteOIDCClientRequest { ItemId = existing.ClientId };
                    // Call repository directly to avoid recursive sync logic
                    await _authenticationRepository.DeleteOidcClientAsync(deleteRequest);
                }
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> UpdateIdentityProviderStatusAsync(string id, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "id_required", "Provider ID is required." } } };

            var existing = await _authenticationRepository.GetIdentityProviderByIdAsync(id);
            if (existing == null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", "Provider not found." } } };

            existing.IsActive = isActive;
            await _authenticationRepository.UpdateIdentityProviderAsync(existing);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<RotateOidcClientSecretResponse> RotateOidcClientSecretAsync(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return new RotateOidcClientSecretResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "id_required", "ItemId is required." } } };

            var credential = await _authenticationRepository.GetOidcClientRegistrationAsync(itemId);
            if (credential == null)
                return new RotateOidcClientSecretResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", $"OIDC client '{itemId}' not found." } } };

            var blocksContext = BlocksContext.GetContext();
            var rotatedAt = DateTime.UtcNow;
            credential.ClientSecret = GenerateClientSecret();
            credential.LastUpdatedBy = blocksContext?.UserId;
            credential.LastUpdatedDate = rotatedAt;

            await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);

            return new RotateOidcClientSecretResponse
            {
                IsSuccess = true,
                ItemId = credential.ItemId,
                ClientId = credential.ClientId,
                ClientSecret = credential.ClientSecret,
                RotatedAt = rotatedAt,
                RotatedBy = blocksContext?.UserId,
            };
        }
    }
}
