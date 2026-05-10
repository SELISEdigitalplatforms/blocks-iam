using Blocks.Genesis;
using DeviceDetectorNET;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.ResponseModel;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
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
    public class AuthenticationDomainService : IAuthenticationDomainService
    {
        private readonly IMessageClient _messageClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IConfiguration _configuration;
        private readonly IUserRepository _userRepository;
        private readonly IValidator<SaveSsoCredentialRequest> _validator;
        private readonly ITenants _tenants;


        private readonly static HttpClient _httpClient = new();

        private const string Origin_Header_Name = "Origin";
        private const string Referer_Header_Name = "Referer";
        private const string X_Forwarded_For_Header_Name = "X-Forwarded-For";

        public AuthenticationDomainService(IMessageClient messageClient,
                                           IAuthenticationRepository authenticationRepository,
                                           IConfiguration configuration,
                                           IUserRepository userRepository,
                                           IValidator<SaveSsoCredentialRequest> validator,
                                           ITenants tenants)
        {
            _messageClient = messageClient;
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _userRepository = userRepository;
            _validator = validator;
            _tenants = tenants;
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

        public static async Task<OpenIdConnectConfiguration?> GetMetadataAsync(string wellKnownUrl)
        {
            var response = await _httpClient.GetAsync(wellKnownUrl);
            string json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<OpenIdConnectConfiguration>(json);
        }

        public async Task<SaveOIDCClientResponse> SaveOIDCClientAsync(SaveOIDCClientRequest request)
        {
            var credential = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ItemId ?? "");
            var clientId = request.ItemId;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientId = Guid.NewGuid().ToString();
            }

            credential = credential ?? new OidcClientRegistration
            {
                ItemId = clientId,
                ClientId = clientId,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
            };

            credential.ItemId = clientId;
            credential.ClientId = clientId;

            credential.ClientSecret = string.IsNullOrWhiteSpace(credential.ClientSecret)
                ? Guid.NewGuid().ToString("n")
                : credential.ClientSecret;

            var allowedScopes = request.AllowedScopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allowedScopes.Count == 0 && !string.IsNullOrWhiteSpace(request.Scope))
            {
                allowedScopes = request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            var redirectUris = request.RedirectUris.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (redirectUris.Count == 0 && !string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                redirectUris = [request.RedirectUri];
            }

            var allowedServiceAccessResources = request.AllowedServiceAccessResources.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allowedServiceAccessResources.Count == 0 && !string.IsNullOrWhiteSpace(request.ServiceAccessResource))
            {
                allowedServiceAccessResources = [request.ServiceAccessResource];
            }

            credential.AllowedScopes = allowedScopes;
            credential.Scope = string.Join(' ', allowedScopes);

            credential.RedirectUris = redirectUris;
            credential.RedirectUri = redirectUris.FirstOrDefault();

            credential.AllowedServiceAccessResources = allowedServiceAccessResources;
            credential.ServiceAccessResource = allowedServiceAccessResources.FirstOrDefault();

            credential.AllowedGrantTypes = request.AllowedGrantTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            credential.AllowedResponseTypes = request.AllowedResponseTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (credential.AllowedResponseTypes.Count == 0)
            {
                credential.AllowedResponseTypes = ["code"];
            }

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
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;
            credential.LogoUri = request.ClientLogoUrl;
            credential.ClientName = request.ClientDisplayName;
            credential.UiBrandColor = request.ClientBrandColor;
            await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);
            
            // Auto-insert/update IdentityProvider for OIDC client
            // Create provider name from client ID or client name
            var providerName = !string.IsNullOrWhiteSpace(request.ClientDisplayName) 
                ? request.ClientDisplayName.ToLower().Replace(" ", "-") 
                : clientId.ToLower();
            
            var existingProvider = await _authenticationRepository.GetIdentityProviderAsync(providerName, IdpConstants.BlocksProviderType);
            
            if (existingProvider == null)
            {
                // Create new IdentityProvider for this OIDC client
                var newProvider = new IdentityProvider
                {
                    Provider = providerName,
                    ProviderType = IdpConstants.BlocksProviderType,
                    Protocol = IdpConstants.OidcProtocol,
                    DisplayName = request.ClientDisplayName ?? clientId,
                    IsActive = request.IsActive,
                    ClientId = credential.ClientId,
                    ClientSecret = credential.ClientSecret,
                    Issuer = request.ExternalDiscoveryEndpoint,
                    WellKnownUrl = request.ExternalDiscoveryEndpoint,
                    AuthorizationUrl = request.ExternalDiscoveryEndpoint ?? "",
                    TokenUrl = request.ExternalDiscoveryEndpoint ?? "",
                    UserInfoUrl = request.ExternalDiscoveryEndpoint ?? "",
                    RedirectUri = credential.RedirectUri,
                    Scope = credential.Scope,
                    ResponseType = "code",
                    GrantTypes = credential.AllowedGrantTypes ?? ["authorization_code", "refresh_token"],
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
                // Update existing provider with latest OIDC client config
                existingProvider.Provider = providerName;
                existingProvider.ProviderType = IdpConstants.BlocksProviderType;
                existingProvider.Protocol = IdpConstants.OidcProtocol;
                existingProvider.ClientId = credential.ClientId;
                existingProvider.ClientSecret = credential.ClientSecret;
                existingProvider.DisplayName = request.ClientDisplayName ?? clientId;
                existingProvider.Issuer = request.ExternalDiscoveryEndpoint;
                existingProvider.WellKnownUrl = request.ExternalDiscoveryEndpoint;
                existingProvider.AuthorizationUrl = request.ExternalDiscoveryEndpoint ?? "";
                existingProvider.TokenUrl = request.ExternalDiscoveryEndpoint ?? "";
                existingProvider.UserInfoUrl = request.ExternalDiscoveryEndpoint ?? "";
                existingProvider.RedirectUri = credential.RedirectUri;
                existingProvider.Scope = credential.Scope;
                existingProvider.GrantTypes = credential.AllowedGrantTypes ?? ["authorization_code", "refresh_token"];
                existingProvider.RequirePkce = credential.RequirePkce;
                existingProvider.TokenEndpointAuthMethod = credential.TokenEndpointAuthMethod;
                existingProvider.IsActive = credential.IsActive;
                await _authenticationRepository.UpdateIdentityProviderAsync(existingProvider);
            }
            
            return new SaveOIDCClientResponse { IsSuccess = true, ItemId = credential.ItemId };
        }

        public async Task<GetOIDCClientResponse> GetOidcClientAsync(string tenantId)
        {
            var client = await _authenticationRepository.GetOIDCCredentialByIdAsync(tenantId);

            return new GetOIDCClientResponse
            {
                oIDCClientCredential = client,
                IsSuccess = true
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
            await _authenticationRepository.DeleteOidcCliantAsync(request);

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

            var normalizedPermissionsByOrg = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (request.PermissionsByOrg != null)
            {
                foreach (var kvp in request.PermissionsByOrg)
                {
                    var orgId = kvp.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(orgId))
                    {
                        return new BaseResponse
                        {
                            IsSuccess = false,
                            Errors = new Dictionary<string, string> { { "invalid_request", "permissions_by_org contains an empty org id." } }
                        };
                    }

                    var normalizedPermissions = (kvp.Value ?? [])
                        .Where(permission => !string.IsNullOrWhiteSpace(permission))
                        .Select(permission => permission.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (normalizedPermissions.Count > 10)
                    {
                        return new BaseResponse
                        {
                            IsSuccess = false,
                            Errors = new Dictionary<string, string>
                            {
                                { "invalid_request", $"A maximum of 10 permissions is allowed per org. Org '{orgId}' has {normalizedPermissions.Count}." }
                            }
                        };
                    }

                    normalizedPermissionsByOrg[orgId] = normalizedPermissions;
                }
            }

            var clientCredential = new ClientCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientSecret = Guid.NewGuid().ToString("n"),
                Name = request.Name,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
                Roles = request.Roles,
                PermissionsByOrg = normalizedPermissionsByOrg,
                IsActive = true,
                Audiences = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.JwtTokenParameters?.Audiences ?? []
            };

            return await _authenticationRepository.SaveClientCredentialAsync(clientCredential);
        }

        public async Task<BaseResponse> DeleteClientCredentialAsync(DeleteClientCredentialRequest request)
        {
            await _authenticationRepository.DeleteClientCredentialAsync(request);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request)
        {
            return await _authenticationRepository.GetClientCredentialsAsync();
        }

        public async Task<BaseResponse> CreateIdentityProviderAsync(IdentityProvider provider)
        {
            if (string.IsNullOrWhiteSpace(provider.Provider))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "provider_required", "Provider name is required." } } };

            if (string.IsNullOrWhiteSpace(provider.DisplayName))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "display_name_required", "Display name is required." } } };

            var existingProvider = await _authenticationRepository.GetIdentityProviderAsync(provider.Provider);
            if (existingProvider != null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "duplicate_provider", $"Provider '{provider.Provider}' already exists." } } };

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

        public async Task<BaseResponse> UpdateIdentityProviderAsync(IdentityProvider provider)
        {
            if (string.IsNullOrWhiteSpace(provider.ItemId))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "id_required", "Provider ID is required." } } };

            var existing = await _authenticationRepository.GetIdentityProviderByIdAsync(provider.ItemId);
            if (existing == null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_found", "Provider not found." } } };

            await _authenticationRepository.UpdateIdentityProviderAsync(provider);
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
                    await _authenticationRepository.DeleteOidcCliantAsync(deleteRequest);
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
    }
}
