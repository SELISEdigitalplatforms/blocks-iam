using Iam.DomainService.Notifications;
using Blocks.Genesis;
using Authentication.DomainService.Utilities;
using DeviceDetectorNET;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Text.Json;
using Authentication.DomainService.Shared.ResponseModel;
using FluentValidation;
using Authentication.DomainService.Shared.RequestModel;
using Iam.DomainService.Dtos;
using Authentication.DomainService.Authentication;
using Blocks.Genesis.Entities.Iam;
using Authentication.DomainService.Shared.Services;
using Microsoft.Extensions.Logging;


namespace Authentication.DomainService.Services
{
    public sealed class AuthenticationDomainService : IAuthenticationDomainService
    {
        private const string ErrorInvalidRequest = "invalid_request";
        private const string ErrorNotFound = "not_found";

        private readonly IMessageClient _messageClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IValidator<SaveOIDCClientRequest> _oidcClientValidator;
        private readonly IValidator<SaveOidcUiTemplateRequest> _oidcUiTemplateValidator;
        private readonly IValidator<SaveIdentityProviderRequest> _saveIdpValidator;
        private readonly IValidator<UpdateIdentityProviderRequest> _updateIdpValidator;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOidcClientTokenLifetimeService? _clientTokenLifetimes;
        private readonly ILogger<AuthenticationDomainService>? _logger;
        private readonly IBuiltInNotificationSender? _notificationSender;

        public const string TokenLifetimesSaveFailedMessage = "Token lifetimes could not be saved. Try again.";

        private const string Origin_Header_Name = "Origin";
        private const string Referer_Header_Name = "Referer";
        private const string DiscoveryClientName = "oidc-discovery";

        public AuthenticationDomainService(IMessageClient messageClient,
                                           IAuthenticationRepository authenticationRepository,
                                           AuthenticationRequestValidators validators,
                                           IHttpClientFactory httpClientFactory,
                                           IOidcClientTokenLifetimeService? clientTokenLifetimes = null,
                                           ILogger<AuthenticationDomainService>? logger = null,
                                           IBuiltInNotificationSender? notificationSender = null)
        {
            _notificationSender = notificationSender;
            _messageClient = messageClient;
            _authenticationRepository = authenticationRepository;
            _oidcClientValidator = validators.OidcClient;
            _oidcUiTemplateValidator = validators.OidcUiTemplate;
            _saveIdpValidator = validators.SaveIdentityProvider;
            _updateIdpValidator = validators.UpdateIdentityProvider;
            _httpClientFactory = httpClientFactory;
            _clientTokenLifetimes = clientTokenLifetimes;
            _logger = logger;
        }

        public IEnumerable<string> GetVisitorsIpAddresses(HttpContext context)
        {
            // The single source of truth is Connection.RemoteIpAddress, which the
            // forwarded-header middleware (Program.cs) rewrites only for connections from
            // trusted networks. The raw X-Forwarded-For header is not read: any entries the
            // middleware leaves behind are untrusted and client-supplied.
            var remoteIpAddress = context.Connection.RemoteIpAddress?.ToString();

            return string.IsNullOrWhiteSpace(remoteIpAddress)
                ? Array.Empty<string>()
                : new[] { remoteIpAddress };
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

            var lifetimeErrors = await ValidateTokenLifetimesAgainstProjectAsync(request.TokenLifetimes);
            if (lifetimeErrors != null)
            {
                return new SaveOIDCClientResponse
                {
                    IsSuccess = false,
                    Errors = lifetimeErrors
                };
            }

            var (credential, isNewClient) = await InitializeOidcClientCredentialAsync(request);
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

                if (credential.RegisterAsIdentityProvider)
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
                            WellKnownUrl = credential.ExternalDiscoveryEndpoint,
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
                        existingProvider.ProviderType = IdpConstants.BlocksOidcProviderType;
                        existingProvider.Protocol = IdpConstants.OidcProtocol;
                        existingProvider.ClientId = credential.ClientId;
                        existingProvider.ClientSecret = credential.ClientSecret;
                        existingProvider.DisplayName = request.ClientDisplayName ?? credential.ClientId;
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

            if (!await TrySaveTokenLifetimesAsync(credential.ClientId, request.TokenLifetimes, isNewClient))
            {
                return new SaveOIDCClientResponse
                {
                    IsSuccess = false,
                    ItemId = credential.ItemId,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(SaveOIDCClientRequest.TokenLifetimes), TokenLifetimesSaveFailedMessage }
                    }
                };
            }

            if (isNewClient)
            {
                await NotifyClientCredentialChangedAsync(OidcClientKind, "created", credential.ClientId, credential.CreatedBy, null);
            }

            // The secret goes back once, on create only. Lists and gets never return it; a lost
            // secret is replaced with rotate-secret.
            return new SaveOIDCClientResponse
            {
                IsSuccess = true,
                ItemId = credential.ItemId,
                ClientSecret = isNewClient ? credential.ClientSecret : null
            };
        }

        private const string OidcClientKind = "oidc-client";
        private const string ClientCredentialKind = "client-credential";

        /// <summary>
        /// Tells the acting user and the record's creator that a client was created, its secret
        /// rotated, or it was deleted. Only the client id goes out, never a secret. Best effort.
        /// </summary>
        private Task NotifyClientCredentialChangedAsync(string kind, string change, string? clientId, string? createdBy, string? organizationId)
        {
            var bc = BlocksContext.GetContext();
            var what = kind == OidcClientKind ? "An OIDC client" : "A client credential";
            var description = change switch
            {
                "created" => $"{what} was created.",
                "secret-rotated" => $"{what} had its secret rotated.",
                _ => $"{what} was deleted."
            };

            return _notificationSender.TrySendAsync(new BuiltInNotification(
                bc?.TenantId,
                BuiltInNotificationNames.ClientCredentialChanged,
                BuiltInNotificationSender.Recipients(bc?.UserId, createdBy),
                BuiltInNotificationSender.Payload(
                    BuiltInNotificationNames.ClientCredentialChanged,
                    "Client credential changed",
                    description,
                    fields: new Dictionary<string, object?>
                    {
                        ["kind"] = kind,
                        ["change"] = change,
                        ["clientId"] = clientId,
                        ["organizationId"] = organizationId
                    })));
        }

        /// <summary>
        /// When only one refresh lifetime is set, the other comes from the project; the pair must still
        /// satisfy maximum >= idle. Both-set and range rules live in the request validator.
        /// </summary>
        private async Task<Dictionary<string, string>?> ValidateTokenLifetimesAgainstProjectAsync(OidcClientTokenLifetimes? lifetimes)
        {
            if (lifetimes == null)
            {
                return null;
            }

            var hasSliding = lifetimes.RefreshTokenValidForNumberMinutes.HasValue;
            var hasAbsolute = lifetimes.AbsoluteRefreshTokenValidForNumberMinutes.HasValue;
            if (hasSliding == hasAbsolute)
            {
                return null;
            }

            var project = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var (projectSliding, projectAbsolute) = RefreshTokenLifetimeResolver.Resolve(project);

            if (hasSliding && lifetimes.RefreshTokenValidForNumberMinutes!.Value > projectAbsolute)
            {
                return new Dictionary<string, string>
                {
                    {
                        $"{nameof(SaveOIDCClientRequest.TokenLifetimes)}.{nameof(OidcClientTokenLifetimes.RefreshTokenValidForNumberMinutes)}",
                        $"Refresh token idle lifetime must not exceed the project's maximum refresh token lifetime ({projectAbsolute} minutes)."
                    }
                };
            }

            if (hasAbsolute && lifetimes.AbsoluteRefreshTokenValidForNumberMinutes!.Value < projectSliding)
            {
                return new Dictionary<string, string>
                {
                    {
                        $"{nameof(SaveOIDCClientRequest.TokenLifetimes)}.{nameof(OidcClientTokenLifetimes.AbsoluteRefreshTokenValidForNumberMinutes)}",
                        $"Refresh token maximum lifetime must be at least the project's refresh token idle lifetime ({projectSliding} minutes)."
                    }
                };
            }

            return null;
        }

        /// <summary>
        /// Absent lifetimes keep an existing client's stored values; a new client never inherits an entry
        /// left under the same id. Runs after the client document is saved; a failure is reported to the
        /// caller and the previous values stay in force.
        /// </summary>
        private async Task<bool> TrySaveTokenLifetimesAsync(string clientId, OidcClientTokenLifetimes? lifetimes, bool isNewClient)
        {
            if (_clientTokenLifetimes == null || (lifetimes == null && !isNewClient))
            {
                return true;
            }

            try
            {
                if (lifetimes == null)
                {
                    await _clientTokenLifetimes.DeleteAsync(clientId);
                }
                else
                {
                    await _clientTokenLifetimes.SaveAsync(clientId, lifetimes);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Saving OIDC client token lifetimes failed. clientId={ClientId}", clientId);
                return false;
            }
        }

        private async Task<(OidcClientRegistration Credential, bool IsNew)> InitializeOidcClientCredentialAsync(SaveOIDCClientRequest request)
        {
            var credential = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ItemId ?? "");
            var isNew = credential == null;
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
            // Device-flow clients are always public (TokenEndpointAuthMethod "none") and never
            // authenticate with a secret, so none is generated — and any secret left over from
            // before the client became device-flow-only is cleared.
            if (request.IsDeviceFlowClient)
            {
                credential.ClientSecret = null;
            }
            else if (string.IsNullOrWhiteSpace(credential.ClientSecret))
            {
                credential.ClientSecret = GenerateClientSecret();
            }

            return (credential, isNew);
        }

        private static void ApplyOidcClientScopesAndRedirects(OidcClientRegistration credential, SaveOIDCClientRequest request)
        {
            var allowedScopes = request.AllowedScopes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allowedScopes.Count == 0 && !string.IsNullOrWhiteSpace(request.Scope))
            {
                allowedScopes = request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (!allowedScopes.Any(scope => string.Equals(scope, "offline_access", StringComparison.OrdinalIgnoreCase)))
            {
                allowedScopes.Add("offline_access");
            }

            var redirectUris = request.IsDeviceFlowClient
                ? []
                : request.RedirectUris.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            credential.AllowedScopes = allowedScopes;
            credential.Scope = string.Join(' ', allowedScopes);

            credential.RedirectUris = redirectUris;
           // credential.RedirectUri = redirectUris.FirstOrDefault();

            credential.AllowedResponseTypes = request.IsDeviceFlowClient
                ? []
                : request.AllowedResponseTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!request.IsDeviceFlowClient && credential.AllowedResponseTypes.Count == 0)
            {
                credential.AllowedResponseTypes = ["code"];
            }
        }

        private static void ApplyOidcClientOptions(OidcClientRegistration credential, SaveOIDCClientRequest request)
        {
            credential.PostLogoutRedirectUris = request.PostLogoutRedirectUris.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // ExternalDiscoveryEndpoint only feeds the RegisterAsIdentityProvider branch's
            // WellKnownUrl, which is itself forced off for device-flow clients below — so it
            // can never take effect for one. Force off here for the same reason.
            credential.ExternalDiscoveryEndpoint = request.IsDeviceFlowClient ? null : request.ExternalDiscoveryEndpoint;
            credential.LoginMode = request.LoginMode;
            // Device-flow clients (RFC 8628) are inherently public/native clients — they cannot hold a
            // secret safely, and the FE registration form has no way to opt them into "confidential".
            // Force public here so this stays in sync with OidcClientValidator.IsPublicClient, which
            // already treats every device-flow client as public regardless of ClientType.
            credential.ClientType = request.IsDeviceFlowClient ? "public" : request.ClientType;
            credential.TokenEndpointAuthMethod = request.IsDeviceFlowClient
                || string.Equals(request.ClientType, "public", StringComparison.OrdinalIgnoreCase)
                ? "none"
                : "client_secret_post";
            // PKCE (RFC 7636) has no meaning for device flow (RFC 8628) — there is no browser
            // redirect/authorize step to protect. Force it off server-side rather than trusting
            // whatever the FE form happens to submit.
            credential.RequirePkce = !request.IsDeviceFlowClient && request.RequirePkce;
            credential.RequireConsent = request.RequireConsent;
            // Front-channel logout redirects the browser back to the client's logout endpoint —
            // device-flow clients have no browser session to redirect. Back-channel logout is a
            // server-to-server call and still applies, so it's left alone.
            credential.FrontChannelLogoutUri = request.IsDeviceFlowClient ? null : request.FrontChannelLogoutUri;
            credential.BackChannelLogoutUri = request.BackChannelLogoutUri;
            credential.IsActive = request.IsActive;
            credential.IsAutoRedirect = request.IsAutoRedirect;
            credential.UseTokensCookie = request.UseTokensCookie;
            credential.RequireMfa = request.RequireMfa;
            credential.AllowedMfaMethods = request.AllowedMfaMethods;
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;
            credential.ClientName = request.ClientDisplayName;
            credential.IsDeviceFlowClient = request.IsDeviceFlowClient;
            // Federating this client as an upstream IdentityProvider hardcodes the
            // authorization_code grant and reuses RedirectUris, which device-flow clients
            // never have — the two features are incompatible. Force off for device flow.
            credential.RegisterAsIdentityProvider = !request.IsDeviceFlowClient && request.RegisterAsIdentityProvider;
        }

        private static string ResolveTokenLifetimesKey(OidcClientRegistration client) =>
            string.IsNullOrWhiteSpace(client.ClientId) ? client.ItemId : client.ClientId;

        public async Task<GetOIDCClientResponse> GetOidcClientAsync(string tenantId)
        {
            var client = await _authenticationRepository.GetOIDCCredentialByIdAsync(tenantId);
            var tokenLifetimes = client != null && _clientTokenLifetimes != null
                ? await _clientTokenLifetimes.GetAsync(ResolveTokenLifetimesKey(client))
                : null;
            HideSecret(client);
            return new GetOIDCClientResponse
            {
                oIDCClientCredential = client,
                TokenLifetimes = tokenLifetimes,
                IsSuccess = true,
            };
        }

        public async Task<GetOIDCClientsResponse> GetOidcClientsAsync()
        {
            var clients = await _authenticationRepository.GetOIDCCredentialsByTenantForViewAsync();
            clients?.ForEach(HideSecret);

            return new GetOIDCClientsResponse
            {
                oIDCClientCredentials = clients ?? [],
                IsSuccess = true
            };
        }

        public async Task<OidcUiTemplate?> GetOidcTemplateForManagementAsync()
        {
            return await _authenticationRepository.GetOidcUiTemplateAsync();
        }

        public async Task<SaveOidcUiTemplateResponse> SaveOidcUiTemplateRequestAsync(SaveOidcUiTemplateRequest request)
        {
            var validation = await _oidcUiTemplateValidator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                return new SaveOidcUiTemplateResponse
                {
                    IsSuccess = false,
                    Errors = validation.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.First().ErrorMessage)
                };
            }

            var template = new OidcUiTemplate
            {
                ItemId = Guid.NewGuid().ToString(),
                SchemaVersion = OidcUiTemplate.CurrentSchemaVersion,
                Branding = request.Branding,
                Theme = request.Theme,
                Pages = request.Pages
            };

            try
            {
                await _authenticationRepository.SaveOidcUiTemplateAsync(template);
            }
            catch (Exception)
            {
                return new SaveOidcUiTemplateResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Template", "Failed to save OIDC UI template." }
                    }
                };
            }

            return new SaveOidcUiTemplateResponse
            {
                IsSuccess = true,
                ItemId = template.ItemId
            };
        }

        public async Task<BaseResponse> DeleteOidcClientAsync(DeleteOIDCClientRequest request)
        {
            // Get the OIDC client before deletion to know the ClientId
            var credential = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ItemId ?? "");

            // The client's token lifetimes go first: if this fails the client stays, so no values are
            // left behind for a later client created under the same id.
            if (_clientTokenLifetimes != null)
            {
                var lifetimesKey = !string.IsNullOrWhiteSpace(credential?.ClientId) ? credential.ClientId : request.ItemId;
                await _clientTokenLifetimes.DeleteAsync(lifetimesKey ?? string.Empty);
            }

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

            if (credential != null)
            {
                await NotifyClientCredentialChangedAsync(OidcClientKind, "deleted", credential.ClientId, credential.CreatedBy, null);
            }

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<SaveClientCredentialResponse> SaveClientCredentialAsync(SaveClientCredentialRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return ClientCredentialSaveFailure(ErrorInvalidRequest, "Name is required.");

            var roles = (request.Roles ?? new List<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var permissions = (request.Permissions ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (roles.Count == 0 && permissions.Count == 0)
                return ClientCredentialSaveFailure(ErrorInvalidRequest, "At least one role or permission is required.");

            var blocksContext = BlocksContext.GetContext();
            var scope = OrganizationAccessScopeResolver.Resolve(blocksContext?.OrganizationId);
            if (scope.Kind == OrganizationAccessScopeKind.Denied)
                return ClientCredentialSaveFailure("forbidden", "Your token names no organization, so client credentials cannot be managed.");

            var isUpdate = !string.IsNullOrWhiteSpace(request.ItemId);
            if (isUpdate)
            {
                var existing = await _authenticationRepository.GetClientCredentialByIdAsync(request.ItemId!);

                // Out of scope is reported as "not found", decided before anything is written, so a
                // caller learns nothing about credentials belonging to another organization.
                if (existing == null || !scope.Allows(existing.OrganizationId))
                    return ClientCredentialSaveFailure(ErrorNotFound, $"Client credential '{request.ItemId}' not found.");

                existing.Name = request.Name;
                existing.IsActive = request.IsActive;
                existing.AccessTokenValidForNumberMinutes = request.AccessTokenValidForNumberMinutes;
                existing.LastUpdatedBy = blocksContext?.UserId;
                existing.LastUpdatedDate = DateTime.UtcNow;
                existing.Roles = roles;
                existing.Permissions = permissions;

                // OrganizationId is deliberately not updated. Re-scoping a live credential would
                // change the reach of tokens that running services already hold, without those
                // services ever asking for it.
                var saveResult = await _authenticationRepository.SaveClientCredentialAsync(existing);
                return new SaveClientCredentialResponse
                {
                    IsSuccess = saveResult.IsSuccess,
                    Errors = saveResult.Errors,
                    ItemId = existing.ItemId
                };
            }

            var (organizationId, organizationError) = ResolveNewCredentialOrganization(scope, request.OrganizationId);
            if (organizationError != null)
                return new SaveClientCredentialResponse
                {
                    IsSuccess = organizationError.IsSuccess,
                    Errors = organizationError.Errors
                };

            var clientCredential = new ClientCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientSecret = ClientSecretGenerator.Generate(),
                Name = request.Name,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedBy = blocksContext?.UserId,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                OrganizationId = organizationId!,
                AccessTokenValidForNumberMinutes = request.AccessTokenValidForNumberMinutes,
                Roles = roles,
                Permissions = permissions,
                IsActive = request.IsActive
            };

            var createResult = await _authenticationRepository.SaveClientCredentialAsync(clientCredential);
            if (createResult.IsSuccess)
            {
                // A client credential's item id is its client id.
                await NotifyClientCredentialChangedAsync(ClientCredentialKind, "created", clientCredential.ItemId, clientCredential.CreatedBy, clientCredential.OrganizationId);
            }
            return new SaveClientCredentialResponse
            {
                IsSuccess = createResult.IsSuccess,
                Errors = createResult.Errors,
                ItemId = clientCredential.ItemId,
                ClientSecret = createResult.IsSuccess ? clientCredential.ClientSecret : null
            };
        }

        public async Task<RotateClientCredentialSecretResponse> RotateClientCredentialSecretAsync(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return new RotateClientCredentialSecretResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorInvalidRequest, "ItemId is required." } } };

            var scope = OrganizationAccessScopeResolver.Resolve(BlocksContext.GetContext()?.OrganizationId);
            if (scope.Kind == OrganizationAccessScopeKind.Denied)
                return new RotateClientCredentialSecretResponse { IsSuccess = false, Errors = ClientCredentialForbidden().Errors };

            var credential = await _authenticationRepository.GetClientCredentialByIdAsync(itemId);
            if (credential == null || !scope.Allows(credential.OrganizationId))
                return new RotateClientCredentialSecretResponse { IsSuccess = false, Errors = ClientCredentialNotFound(itemId).Errors };

            credential.ClientSecret = ClientSecretGenerator.Generate();
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;

            var saveResult = await _authenticationRepository.SaveClientCredentialAsync(credential);
            if (saveResult.IsSuccess)
            {
                await NotifyClientCredentialChangedAsync(ClientCredentialKind, "secret-rotated", credential.ItemId, credential.CreatedBy, credential.OrganizationId);
            }

            return new RotateClientCredentialSecretResponse
            {
                IsSuccess = saveResult.IsSuccess,
                Errors = saveResult.Errors,
                ClientSecret = saveResult.IsSuccess ? credential.ClientSecret : null
            };
        }

        /// <summary>
        /// Which organization a newly created client credential is issued for.
        /// </summary>
        /// <remarks>
        /// The credential's organization becomes the <c>organization_id</c> claim on every token the
        /// client_credentials grant later mints for it, so it is exactly as privileged as the value
        /// stored here. The token endpoint authenticates a client id and secret with no caller
        /// context of its own, and therefore cannot re-check any of this -- which makes create the
        /// only place the decision can be made.
        /// <para>
        /// A caller pinned to one organization cannot issue outside it, and its payload is discarded
        /// rather than rejected, matching <see cref="ResourceWriteOrganizationScope"/>. Only the
        /// tenant-wide caller may choose, and choosing nothing keeps the previous behaviour of a
        /// tenant-wide credential.
        /// </para>
        /// </remarks>
        private static (string? OrganizationId, BaseResponse? Error) ResolveNewCredentialOrganization(
            OrganizationAccessScope scope,
            string? requestedOrganizationId)
        {
            if (scope.Kind == OrganizationAccessScopeKind.Organization)
                return (scope.OrganizationId, null);

            var requested = requestedOrganizationId?.Trim();
            if (string.IsNullOrWhiteSpace(requested))
                return (IdpConstants.DefaultOrganizationId, null);

            // "no-org" is the sentinel for "belongs to nothing". It is a real stored claim value, so
            // accepting it here would mint tokens carrying it as though it named a place.
            if (string.Equals(requested, IdpConstants.NoOrganizationId, StringComparison.Ordinal))
                return (null, new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { ErrorInvalidRequest, $"'{IdpConstants.NoOrganizationId}' is not a selectable organization." } }
                });

            return (requested, null);
        }

        private static BaseResponse ClientCredentialNotFound(string? itemId) =>
            new()
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { ErrorNotFound, $"Client credential '{itemId}' not found." } }
            };

        private static BaseResponse ClientCredentialForbidden() =>
            new()
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "forbidden", "Your token names no organization, so client credentials cannot be managed." } }
            };

        private static SaveClientCredentialResponse ClientCredentialSaveFailure(string key, string message) =>
            new()
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { key, message } }
            };

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
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorInvalidRequest, "ItemId is required." } } };

            var scope = OrganizationAccessScopeResolver.Resolve(BlocksContext.GetContext()?.OrganizationId);
            if (scope.Kind == OrganizationAccessScopeKind.Denied)
                return ClientCredentialForbidden();

            var existing = await _authenticationRepository.GetClientCredentialByIdAsync(request.ItemId);
            if (existing == null || !scope.Allows(existing.OrganizationId))
                return ClientCredentialNotFound(request.ItemId);

            await _authenticationRepository.DeleteClientCredentialAsync(request);
            await NotifyClientCredentialChangedAsync(ClientCredentialKind, "deleted", existing.ItemId, existing.CreatedBy, existing.OrganizationId);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request)
        {
            // Scoped so one organization's administrator cannot see another organization's credentials.
            // The secret itself is never listed: it is returned once on create and on rotate-secret.
            var scope = OrganizationAccessScopeResolver.Resolve(BlocksContext.GetContext()?.OrganizationId);

            var credentials = scope.Kind switch
            {
                OrganizationAccessScopeKind.AllOrganizations => await _authenticationRepository.GetClientCredentialsAsync(null),
                OrganizationAccessScopeKind.Organization => await _authenticationRepository.GetClientCredentialsAsync(scope.OrganizationId),
                _ => []
            };
            credentials.ForEach(HideSecret);
            return credentials;
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
                ClientSecret = request.ClientSecret,
                Issuer = request.Issuer,
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
            var provider = await _authenticationRepository.GetIdentityProviderByIdForViewAsync(id);
            HideSecret(provider);
            return provider;
        }

        public async Task<List<IdentityProvider>> GetAllIdentityProvidersAsync()
        {
            var providers = await _authenticationRepository.GetIdentityProvidersForViewAsync();
            providers.ForEach(HideSecret);
            return providers;
        }

        private async Task<bool> IsMirrorOfLocalOidcClientAsync(IdentityProvider provider)
        {
            if (!string.Equals(provider.ProviderType, IdpConstants.BlocksOidcProviderType, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(provider.ClientId))
                return false;
            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(provider.ClientId);
            return client?.RegisterAsIdentityProvider == true;
        }

        // Read results that go back to the API caller never carry a secret. These objects come
        // from a fresh "ForView" read and are never saved back, so clearing them changes nothing stored.
        private static void HideSecret(OidcClientRegistration? client)
        {
            if (client != null) client.ClientSecret = null;
        }

        private static void HideSecret(ClientCredential credential) => credential.ClientSecret = null;

        private static void HideSecret(IdentityProvider? provider)
        {
            if (provider == null) return;
            provider.ClientSecret = string.Empty;
            provider.PrivateKey = null;
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
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorNotFound, "Provider not found." } } };

            // Immutability check: Provider, ProviderType, Protocol, ClientId cannot change after create.
            if (request.Provider != null && !string.Equals(request.Provider, existing.Provider, StringComparison.OrdinalIgnoreCase))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "Provider is immutable and must match the existing value." } } };
            if (request.ProviderType != null && !string.Equals(request.ProviderType, existing.ProviderType, StringComparison.OrdinalIgnoreCase))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "ProviderType is immutable and must match the existing value." } } };
            if (request.Protocol != null && !string.Equals(request.Protocol, existing.Protocol, StringComparison.OrdinalIgnoreCase))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "Protocol is immutable and must match the existing value." } } };
            if (request.ClientId != null && !string.Equals(request.ClientId, existing.ClientId, StringComparison.Ordinal))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "ClientId is immutable and must match the existing value." } } };

            // A provider made by "register as identity provider" mirrors its OIDC client's secret;
            // that secret changes only by rotating the OIDC client, or the next client save would undo it.
            if (!string.IsNullOrWhiteSpace(request.ClientSecret) && await IsMirrorOfLocalOidcClientAsync(existing))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "immutable_field", "This provider uses its OIDC client's secret. Rotate the secret on the OIDC client instead." } } };

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
            if (!string.IsNullOrWhiteSpace(request.PrivateKey)) existing.PrivateKey = request.PrivateKey;
            if (!string.IsNullOrWhiteSpace(request.ClientSecret)) existing.ClientSecret = request.ClientSecret;
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
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorNotFound, "Provider not found." } } };

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
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorNotFound, "Provider not found." } } };

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
                return new RotateOidcClientSecretResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorNotFound, $"OIDC client '{itemId}' not found." } } };

            // Device-flow clients are always public (TokenEndpointAuthMethod "none") and never
            // hold a secret — reject rotation rather than silently reintroducing one.
            if (credential.IsDeviceFlowClient)
                return new RotateOidcClientSecretResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "not_supported", "Device-flow clients do not use a client secret." } } };

            var blocksContext = BlocksContext.GetContext();
            var rotatedAt = DateTime.UtcNow;
            credential.ClientSecret = GenerateClientSecret();
            credential.LastUpdatedBy = blocksContext?.UserId;
            credential.LastUpdatedDate = rotatedAt;

            await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);

            // The linked Blocks IdentityProvider keeps its own copy of the secret, so a rotation
            // that only wrote OidcClientRegistrations would leave federation presenting the old
            // one at the token endpoint. Keep the mirror current — same ClientId link the
            // RegisterAsIdentityProvider sync in SaveOIDCClientAsync uses.
            if (credential.RegisterAsIdentityProvider && !string.IsNullOrWhiteSpace(credential.ClientId))
            {
                var linkedProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(credential.ClientId);
                if (linkedProvider != null && !string.IsNullOrWhiteSpace(linkedProvider.ItemId))
                {
                    linkedProvider.ClientSecret = credential.ClientSecret;
                    await _authenticationRepository.UpdateIdentityProviderAsync(linkedProvider);
                }
            }

            await NotifyClientCredentialChangedAsync(OidcClientKind, "secret-rotated", credential.ClientId, credential.CreatedBy, null);

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
