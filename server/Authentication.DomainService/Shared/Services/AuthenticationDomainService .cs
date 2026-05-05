using Blocks.Genesis;
using DeviceDetectorNET;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.ResponseModel;
using Authentication.DomainService.Shared;
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

        public async Task<SaveSsoCredentialResponse> SaveSocialLoginCredentialAsync(SaveSsoCredentialRequest credential)
        {
            var validationResult = await _validator.ValidateAsync(credential);

            if (!validationResult.IsValid)
            {
                return new SaveSsoCredentialResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var loginCredential = await _authenticationRepository.GetSocialLoginCredentialByIdAsync(credential?.ItemId ?? "");
            var repoCredential = await MapToSocialLoginCredential(loginCredential, credential ?? new SaveSsoCredentialRequest());
            await _authenticationRepository.SaveSocialLoginCredentialAsync(repoCredential);

            return new SaveSsoCredentialResponse { IsSuccess = true, ItemId = repoCredential.ItemId };
        }

        public static async Task<OpenIdConnectConfiguration?> GetMetadataAsync(string wellKnownUrl)
        {
            var response = await _httpClient.GetAsync(wellKnownUrl);
            string json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<OpenIdConnectConfiguration>(json);
        }

        public async Task<BaseResponse> DeleteSocialLoginCredentialAsync(string itemId)
        {
            await _authenticationRepository.DeleteSocialLoginCredentialAsync(itemId);
            return new BaseResponse { IsSuccess = true, };
        }

        public async Task<GetSsoCredentialResponse> GetSsoCredentialAsync(string itemId)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByIdAsync(itemId);

            var roles = await _userRepository.GetRolesBySlugsAsync(credential.InitialRoles);
            var permissions = await _userRepository.GetPermissionsByResourcesAsync(credential.InitialPermissions);

            var response = GetResponse(credential);
            response.UserRoles = roles;
            response.UserPermissions = permissions;

            return response;
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

            var allowedAudiences = request.AllowedAudiences.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allowedAudiences.Count == 0 && !string.IsNullOrWhiteSpace(request.Audience))
            {
                allowedAudiences = [request.Audience];
            }

            credential.AllowedScopes = allowedScopes;
            credential.Scope = string.Join(' ', allowedScopes);

            credential.RedirectUris = redirectUris;
            credential.RedirectUri = redirectUris.FirstOrDefault();

            credential.AllowedAudiences = allowedAudiences;
            credential.Audience = allowedAudiences.FirstOrDefault();

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
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;
            credential.LogoUri = request.ClientLogoUrl;
            credential.ClientName = request.ClientDisplayName;
            credential.UiBrandColor = request.ClientBrandColor;
            await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);
            return new SaveOIDCClientResponse { IsSuccess = true, ItemId = credential.ItemId };
        }

        public async Task<GetOIDCClientResponse> GetOIDCClientAsyncAsync(string tenantId)
        {
            var client = await _authenticationRepository.GetOIDCCredentialByIdAsync(tenantId);

            return new GetOIDCClientResponse
            {
                oIDCClientCredential = client,
                IsSuccess = true
            };
        }

        public async Task<GetOIDCClientsResponse> GetOIDCClientsAsyncAsync()
        {
            var clients = await _authenticationRepository.GetOIDCCredentialsByTenantAsync();

            return new GetOIDCClientsResponse
            {
                oIDCClientCredentials = clients ?? [],
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> DeleteOIDCClientAsyncAsync(DeleteOIDCClientRequest request)
        {
            await _authenticationRepository.DeleteOidcCliantAsync(request);

            return new BaseResponse { IsSuccess = true };
        }

        private GetSsoCredentialResponse GetResponse(SocialLoginCredential socialLoginCredential)
        {
            return new GetSsoCredentialResponse
            {
                Audience = socialLoginCredential.Audience,
                ClientId = socialLoginCredential.ClientId,
                ClientSecret = socialLoginCredential.ClientSecret,
                Provider = socialLoginCredential.Provider,
                RedirectUrl = socialLoginCredential.RedirectUrl,
                AuthorizationUrl = socialLoginCredential.AuthorizationUrl,
                WellKnownUrl = socialLoginCredential.WellKnownUrl,
                TokenUrl = socialLoginCredential.TokenUrl,
                GetProfileUrl = socialLoginCredential.GetProfileUrl,
                Scope = socialLoginCredential.Scope,
                ItemId = socialLoginCredential.ItemId,
                CreatedBy = socialLoginCredential.CreatedBy,
                LastUpdatedBy = socialLoginCredential.LastUpdatedBy,
                CreatedDate = socialLoginCredential.CreatedDate,
                LastUpdatedDate = socialLoginCredential.LastUpdatedDate
            };
        }

        private async Task<SocialLoginCredential> MapToSocialLoginCredential(SocialLoginCredential credential, SaveSsoCredentialRequest saveSocialLoginCredentialRequest)
        {
            var now = DateTime.UtcNow;
            var userId = BlocksContext.GetContext()?.UserId;
            var metaData = !string.IsNullOrWhiteSpace(saveSocialLoginCredentialRequest.WellKnownUrl) ? await GetMetadataAsync(saveSocialLoginCredentialRequest.WellKnownUrl) : null;

            credential ??= new SocialLoginCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = userId,
                CreatedDate = now,
                LastUpdatedDate = now,
                LastUpdatedBy = userId,
                ClientSecret = saveSocialLoginCredentialRequest.ClientSecret,
                ClientId = saveSocialLoginCredentialRequest.ClientId,
                Provider = saveSocialLoginCredentialRequest.Provider,
                Audience = saveSocialLoginCredentialRequest.Audience,
                AuthorizationUrl = metaData?.AuthorizationEndpoint ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:AuthorizationUrl"] ?? "",
                TokenUrl = metaData?.TokenEndpoint ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:TokenUrl"] ?? "",
                GetProfileUrl = metaData?.UserInfoEndpoint ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:GetProfileUrl"] ?? "",
                GetEmailUrl = _configuration[$"{saveSocialLoginCredentialRequest.Provider}:GetEmailUrl"] ?? "",
                RedirectUrl = saveSocialLoginCredentialRequest.RedirectUrl,
                WellKnownUrl = saveSocialLoginCredentialRequest.WellKnownUrl,
                Scope = metaData?.ScopesSupported.ToString() ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:Scope"] ?? ""
            };

            credential.Audience = saveSocialLoginCredentialRequest.Audience;
            credential.ClientId = saveSocialLoginCredentialRequest.ClientId;
            credential.ClientSecret = saveSocialLoginCredentialRequest.ClientSecret;
            credential.RedirectUrl = saveSocialLoginCredentialRequest.RedirectUrl;
            credential.WellKnownUrl = saveSocialLoginCredentialRequest.WellKnownUrl;
            credential.Provider = saveSocialLoginCredentialRequest.Provider;
            credential.LastUpdatedDate = now;
            credential.LastUpdatedBy = userId;
            credential.InitialRoles = saveSocialLoginCredentialRequest.InitialRoles;
            credential.InitialPermissions = saveSocialLoginCredentialRequest.InitialPermissions;
            credential.IsDisabled = saveSocialLoginCredentialRequest.IsDisabled;
            credential.SSOType = saveSocialLoginCredentialRequest.SSOType;
            credential.TeamId = saveSocialLoginCredentialRequest.TeamId;
            credential.KeyId = saveSocialLoginCredentialRequest.KeyId;
            credential.PrivateKey = saveSocialLoginCredentialRequest.PrivateKey;
            credential.AppleAudience = _configuration[$"{saveSocialLoginCredentialRequest.Provider}:AppleAudience"] ?? "";

            return credential;
        }

        public async Task<List<SocialLoginCredential>> GetSocialLoginCredentialsAsync()
        {
            return await _authenticationRepository.GetSocialLoginCredentialsAsync();
        }

        public async Task<BaseResponse> UpdateSsoCredentialStatusAsync(UpdateSsoCredentialStatusRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ItemId))
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "empty_item_id", "ItemId should not be empty" } } };
            }

            var updates = new Dictionary<string, object>
                          {
                             { nameof(SocialLoginCredential.IsDisabled), request.IsEnabled }
                          };

            await _authenticationRepository.UpdatePartialAsync<SocialLoginCredential>(request.ItemId, updates, "SocialLoginCredentials");

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
    }
}
