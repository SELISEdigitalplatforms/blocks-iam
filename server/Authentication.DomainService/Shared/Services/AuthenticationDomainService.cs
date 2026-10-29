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
using Authentication.DomainService.OAuth;
using Blocks.Secrets;


namespace Authentication.DomainService.Services
{
    public sealed class AuthenticationDomainService : IAuthenticationDomainService
    {
        private const string ErrorInvalidRequest = "invalid_request";
        private const string ErrorNotFound = "not_found";
        private const string ErrorSecretStoreUnavailable = "secret_store_unavailable";
        private const string SecretStoreUnavailableMessage = "The secret store is unavailable. Try again later.";
        private const string SecretValueRejectedMessage = "The value cannot be stored in the secret store.";

        private readonly IMessageClient _messageClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IValidator<SaveOIDCClientRequest> _oidcClientValidator;
        private readonly IValidator<SaveOidcUiTemplateRequest> _oidcUiTemplateValidator;
        private readonly IValidator<SaveIdentityProviderRequest> _saveIdpValidator;
        private readonly IValidator<UpdateIdentityProviderRequest> _updateIdpValidator;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IIdentityProviderSecretResolver _secretResolver;
        private readonly IClientCredentialSecretResolver _clientSecretResolver;
        private readonly IOidcClientSecretResolver _oidcSecretResolver;
        private readonly ILogger<AuthenticationDomainService>? _logger;
        private readonly IBuiltInNotificationSender? _notificationSender;

        private const string Origin_Header_Name = "Origin";
        private const string Referer_Header_Name = "Referer";
        private const string DiscoveryClientName = "oidc-discovery";

        public AuthenticationDomainService(IMessageClient messageClient,
                                           IAuthenticationRepository authenticationRepository,
                                           AuthenticationRequestValidators validators,
                                           IHttpClientFactory httpClientFactory,
                                           IIdentityProviderSecretResolver secretResolver,
                                           IClientCredentialSecretResolver clientSecretResolver,
                                           IOidcClientSecretResolver oidcSecretResolver,
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
            _secretResolver = secretResolver;
            _clientSecretResolver = clientSecretResolver;
            _oidcSecretResolver = oidcSecretResolver;
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
            ApplyOidcClientTokenLifetimes(credential, request.TokenLifetimes, isNewClient);

            OidcClientSecretWrite secretWrite;
            try
            {
                secretWrite = await PrepareOidcClientSecretAsync(credential, request.IsDeviceFlowClient);
            }
            catch (SecretVaultException)
            {
                return new SaveOIDCClientResponse { IsSuccess = false, Errors = SecretStoreUnavailable().Errors };
            }

            try
            {
                await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);
            }
            catch
            {
                // The upsert may still have committed: the secret created here is retired only when
                // the stored document does not point at it.
                await RetireUnreferencedOidcClientSecretAsync(credential, secretWrite.CreatedReference, keepWhenMissing: !isNewClient);
                throw;
            }

            // A client that became device-flow dropped its secret.
            await RetireUnreferencedOidcClientSecretAsync(credential, secretWrite.DroppedReference, keepWhenMissing: true);

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
                            // The provider shares its client's secret reference: it never holds the
                            // plaintext, and a rotation of the client reaches it.
                            ClientSecret = credential.ClientSecret,
                            ClientSecretReference = credential.ClientSecretReference,
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
                        // The provider shares its client's secret reference instead of a plaintext copy.
                        // A secret of its own (or a legacy copy's) is retired once the update lands.
                        var replacedReference = existingProvider.ClientSecretReference;
                        existingProvider.ClientSecret = credential.ClientSecret;
                        existingProvider.ClientSecretReference = credential.ClientSecretReference;
                        existingProvider.DisplayName = request.ClientDisplayName ?? credential.ClientId;
                        existingProvider.RedirectUris = credential.RedirectUris ?? new List<string>();
                        existingProvider.Scope = credential.Scope;
                        existingProvider.GrantTypes = ["authorization_code", "refresh_token"];
                        existingProvider.RequirePkce = credential.RequirePkce;
                        existingProvider.TokenEndpointAuthMethod = credential.TokenEndpointAuthMethod;
                        existingProvider.IsActive = credential.IsActive;
                        await _authenticationRepository.UpdateIdentityProviderAsync(existingProvider);
                        if (replacedReference != credential.ClientSecretReference)
                            await RetireReplacedSecretAsync(existingProvider, IdentityProviderSecretKind.ClientSecret, replacedReference);
                    }
                }
                else if (existingProvider != null && !string.IsNullOrWhiteSpace(existingProvider.ItemId))
                {
                    // Flag was turned off — remove the previously linked Blocks provider.
                    // Mirrors DeleteOidcClientAsync which also cleans up by ClientId. The secret it
                    // shared stays with the client (see IsLiveOidcClientSecretAsync).
                    await _authenticationRepository.DeleteIdentityProviderAsync(existingProvider.ItemId);
                    await RetireSecretsOfDeletedProviderAsync(existingProvider);
                }
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
                ClientSecret = isNewClient ? secretWrite.Value : null
            };
        }

        /// <summary>The secret side of an OIDC client save, prepared before the document is written.</summary>
        /// <param name="Value">The secret now in the store when this save created one (returned on create only).</param>
        /// <param name="CreatedReference">A secret stored by this save, retired again if the save does not land.</param>
        /// <param name="DroppedReference">A secret the client no longer uses (it became device-flow), retired after the save.</param>
        private sealed record OidcClientSecretWrite(string? Value, string? CreatedReference, string? DroppedReference);

        /// <summary>
        /// Puts the client's secret in Blocks Secrets before the save; the document keeps the
        /// reference only. Secret-store exceptions propagate.
        /// </summary>
        /// <remarks>
        /// A client that already has a reference is left as it is. A client saved before Blocks
        /// Secrets still holds its plaintext: the save moves the same value, so an application
        /// holding it keeps working. A client without a secret (new, or no longer device-flow) gets
        /// a new one, generated by the backend as before.
        /// </remarks>
        private async Task<OidcClientSecretWrite> PrepareOidcClientSecretAsync(OidcClientRegistration credential, bool isDeviceFlowClient)
        {
            if (isDeviceFlowClient)
            {
                // Device-flow clients are always public (TokenEndpointAuthMethod "none") and never
                // authenticate with a secret; one left over from before is dropped.
                var dropped = credential.ClientSecretReference;
                credential.ClientSecret = null;
                credential.ClientSecretReference = null;
                return new OidcClientSecretWrite(null, null, dropped);
            }

            if (!string.IsNullOrEmpty(credential.ClientSecretReference))
                return new OidcClientSecretWrite(null, null, null);

            var value = string.IsNullOrWhiteSpace(credential.ClientSecret) ? GenerateClientSecret() : credential.ClientSecret;
            var reference = await _oidcSecretResolver.StoreAsync(credential, value);
            credential.ClientSecretReference = reference;
            credential.ClientSecret = string.Empty;
            return new OidcClientSecretWrite(value, reference, null);
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
                    }),
                organizationId));
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
        /// Absent lifetimes keep an existing client's values; a new client starts with none. Present lifetimes
        /// replace them, and all-empty lifetimes clear them (back to the project values). Saved with the client.
        /// </summary>
        private static void ApplyOidcClientTokenLifetimes(OidcClientRegistration credential, OidcClientTokenLifetimes? requested, bool isNewClient)
        {
            if (requested == null && !isNewClient)
            {
                return;
            }

            credential.TokenLifetimes = OidcClientTokenLifetimeService.OwnValues(requested);
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
            // PrepareOidcClientSecretAsync applies it, in Blocks Secrets.

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

        public async Task<GetOIDCClientResponse> GetOidcClientAsync(string tenantId)
        {
            var client = await _authenticationRepository.GetOIDCCredentialByIdAsync(tenantId);
            // The view read projects only the schema's fields, and TokenLifetimes is not one of them: read the
            // client's own values from the full document, as token issue does.
            var tokenLifetimes = client == null
                ? null
                : OidcClientTokenLifetimeService.OwnValues((await _authenticationRepository.GetOidcClientRegistrationAsync(client.ItemId))?.TokenLifetimes);
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

            // Delete the OIDC client registration (its token lifetimes are part of it)
            await _authenticationRepository.DeleteOidcClientAsync(request);

            // Delete the related IdentityProvider by ClientId
            if (credential != null && !string.IsNullOrWhiteSpace(credential.ClientId))
            {
                var allProviders = await _authenticationRepository.GetIdentityProvidersAsync();
                var relatedProvider = allProviders?.FirstOrDefault(p => p.ClientId == credential.ClientId);
                
                if (relatedProvider != null && !string.IsNullOrWhiteSpace(relatedProvider.ItemId))
                {
                    await _authenticationRepository.DeleteIdentityProviderAsync(relatedProvider.ItemId);
                    // A linked provider shares this client's secret, which is retired below with the client.
                    if (relatedProvider.ClientSecretReference == credential.ClientSecretReference)
                        relatedProvider.ClientSecretReference = null;
                    await RetireSecretsOfDeletedProviderAsync(relatedProvider);
                }
            }

            if (credential != null)
            {
                await RetireSecretOfDeletedOidcClientAsync(credential);
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

                // A credential saved before Blocks Secrets still holds its plaintext: the edit moves
                // the same value to the secret store, so a client already holding it keeps working.
                string? movedReference = null;
                if (string.IsNullOrEmpty(existing.ClientSecretReference) && !string.IsNullOrEmpty(existing.ClientSecret))
                {
                    try
                    {
                        movedReference = await _clientSecretResolver.StoreAsync(existing, existing.ClientSecret);
                    }
                    catch (SecretVaultException)
                    {
                        return ClientCredentialSaveFailure(ErrorSecretStoreUnavailable, SecretStoreUnavailableMessage);
                    }

                    existing.ClientSecretReference = movedReference;
                    existing.ClientSecret = string.Empty;
                }

                BaseResponse saveResult;
                try
                {
                    saveResult = await _authenticationRepository.SaveClientCredentialAsync(existing);
                }
                catch
                {
                    // The replace may still have committed: the moved secret is retired only when
                    // the stored document does not point at it.
                    await RetireUnreferencedClientSecretAsync(existing, movedReference, keepWhenMissing: true);
                    throw;
                }

                if (!saveResult.IsSuccess)
                    await RetireUnreferencedClientSecretAsync(existing, movedReference, keepWhenMissing: true);

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

            var clientSecret = ClientSecretGenerator.Generate();
            var clientCredential = new ClientCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientSecret = string.Empty,
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

            // The secret goes to Blocks Secrets before the insert; the document keeps the reference only.
            try
            {
                clientCredential.ClientSecretReference = await _clientSecretResolver.StoreAsync(clientCredential, clientSecret);
            }
            catch (SecretVaultException)
            {
                return ClientCredentialSaveFailure(ErrorSecretStoreUnavailable, SecretStoreUnavailableMessage);
            }

            BaseResponse createResult;
            try
            {
                createResult = await _authenticationRepository.SaveClientCredentialAsync(clientCredential);
            }
            catch
            {
                // The insert may still have committed (a timeout after the write), so the secret is
                // retired only when no stored document points at it.
                await RetireUnreferencedClientSecretAsync(clientCredential, clientCredential.ClientSecretReference, keepWhenMissing: false);
                throw;
            }

            if (createResult.IsSuccess)
            {
                // A client credential's item id is its client id.
                await NotifyClientCredentialChangedAsync(ClientCredentialKind, "created", clientCredential.ItemId, clientCredential.CreatedBy, clientCredential.OrganizationId);
            }
            else
            {
                await RetireUnreferencedClientSecretAsync(clientCredential, clientCredential.ClientSecretReference, keepWhenMissing: false);
            }

            return new SaveClientCredentialResponse
            {
                IsSuccess = createResult.IsSuccess,
                Errors = createResult.Errors,
                ItemId = clientCredential.ItemId,
                ClientSecret = createResult.IsSuccess ? clientSecret : null
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

            // Rotated in place when the credential already has a secret in the store; a credential
            // still on plaintext gets a new secret, which moves it to the store.
            var clientSecret = ClientSecretGenerator.Generate();
            string reference;
            bool created;
            try
            {
                (reference, created) = await _clientSecretResolver.ReplaceAsync(credential, clientSecret);
            }
            catch (SecretVaultException)
            {
                return new RotateClientCredentialSecretResponse { IsSuccess = false, Errors = SecretStoreUnavailable().Errors };
            }

            var createdReference = created ? reference : null;
            credential.ClientSecretReference = reference;
            credential.ClientSecret = string.Empty;
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;

            BaseResponse saveResult;
            try
            {
                saveResult = await _authenticationRepository.SaveClientCredentialAsync(credential);
            }
            catch
            {
                await UndoClientSecretReplaceAsync(credential, createdReference);
                throw;
            }

            if (saveResult.IsSuccess)
            {
                await NotifyClientCredentialChangedAsync(ClientCredentialKind, "secret-rotated", credential.ItemId, credential.CreatedBy, credential.OrganizationId);
            }
            else
            {
                await UndoClientSecretReplaceAsync(credential, createdReference);
            }

            return new RotateClientCredentialSecretResponse
            {
                IsSuccess = saveResult.IsSuccess,
                Errors = saveResult.Errors,
                ClientSecret = saveResult.IsSuccess ? clientSecret : null
            };
        }

        /// <summary>
        /// The credential document was not written after a rotate: a secret created for it is
        /// retired when the stored document does not point at it. A rotation in place cannot be
        /// undone (the store already holds the new value), so it is only logged.
        /// </summary>
        private async Task UndoClientSecretReplaceAsync(ClientCredential credential, string? createdReference)
        {
            if (createdReference != null)
            {
                await RetireUnreferencedClientSecretAsync(credential, createdReference, keepWhenMissing: true);
                return;
            }

            _logger?.LogWarning("Client credential {ClientId} was not updated, but its secret rotation cannot be undone.", credential.ItemId);
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

            // Delete the credential, then its secret (a live credential never points at a deleted secret).
            await _authenticationRepository.DeleteClientCredentialAsync(request);
            await RetireSecretOfDeletedClientCredentialAsync(existing);
            await NotifyClientCredentialChangedAsync(ClientCredentialKind, "deleted", existing.ItemId, existing.CreatedBy, existing.OrganizationId);
            return new BaseResponse { IsSuccess = true };
        }

        /// <summary>
        /// Retires the secret of a credential whose document delete was just requested. Data-access
        /// denials make the delete a silent no-op, so the credential is re-read first: the secret
        /// of a credential that still exists is never retired.
        /// </summary>
        private async Task RetireSecretOfDeletedClientCredentialAsync(ClientCredential credential)
        {
            if (string.IsNullOrEmpty(credential.ClientSecretReference))
                return;

            ClientCredential? stored;
            try
            {
                stored = await _authenticationRepository.GetClientCredentialByIdAsync(credential.ItemId);
            }
            catch (Exception ex)
            {
                // The delete itself completed: a failing check must not turn it into an error.
                LogClientCredentialReReadFailed(credential, ex);
                return;
            }

            if (stored != null)
            {
                _logger?.LogWarning("Client credential {ClientId} was not deleted; its secret is kept.", credential.ItemId);
                return;
            }

            await _clientSecretResolver.RetireAsync(credential, credential.ClientSecretReference, CancellationToken.None);
        }

        /// <summary>
        /// After a credential write whose outcome is not known (it threw, or it was not
        /// acknowledged), re-reads the credential and retires <paramref name="reference"/>, a secret
        /// created in this request, when the stored document does not point at it.
        /// </summary>
        /// <remarks>
        /// Fail safe, orphan over dangling, as for identity providers: nothing is retired when the
        /// re-read fails, nor, with <paramref name="keepWhenMissing"/>, when it finds no document.
        /// </remarks>
        private async Task RetireUnreferencedClientSecretAsync(ClientCredential credential, string? reference, bool keepWhenMissing)
        {
            if (string.IsNullOrEmpty(reference))
                return;

            ClientCredential? stored;
            try
            {
                stored = await _authenticationRepository.GetClientCredentialByIdAsync(credential.ItemId);
            }
            catch (Exception ex)
            {
                LogClientCredentialReReadFailed(credential, ex);
                return;
            }

            if (stored == null && keepWhenMissing)
            {
                _logger?.LogWarning("Client credential {ClientId} could not be read back after the update; the secret created by this request is kept.", credential.ItemId);
                return;
            }

            if (stored?.ClientSecretReference == reference)
                return;

            await _clientSecretResolver.RetireAsync(credential, reference, CancellationToken.None);
        }

        // Type only: a repository exception message can carry document values.
        private void LogClientCredentialReReadFailed(ClientCredential credential, Exception ex) =>
            _logger?.LogWarning("Client credential {ClientId} could not be re-read ({FailureClass}); its secret is kept.", credential.ItemId, ex.GetType().Name);

        /// <summary>
        /// Retires the secret of an OIDC client whose document delete was just requested. Data-access
        /// denials make the delete a silent no-op, so the client is re-read first: the secret of a
        /// client that still exists is never retired.
        /// </summary>
        private async Task RetireSecretOfDeletedOidcClientAsync(OidcClientRegistration client)
        {
            if (string.IsNullOrEmpty(client.ClientSecretReference))
                return;

            OidcClientRegistration? stored;
            try
            {
                stored = await _authenticationRepository.GetOidcClientRegistrationAsync(client.ItemId);
            }
            catch (Exception ex)
            {
                // The delete itself completed: a failing check must not turn it into an error.
                LogOidcClientReReadFailed(client, ex);
                return;
            }

            if (stored != null)
            {
                _logger?.LogWarning("OIDC client {ClientId} was not deleted; its secret is kept.", client.ClientId);
                return;
            }

            await _oidcSecretResolver.RetireAsync(client, client.ClientSecretReference, CancellationToken.None);
        }

        /// <summary>
        /// After an OIDC client write whose outcome is not known (it threw), or one that dropped a
        /// secret, re-reads the client and retires <paramref name="reference"/> when the stored
        /// document does not point at it.
        /// </summary>
        /// <remarks>
        /// Fail safe, orphan over dangling, as for client credentials: nothing is retired when the
        /// re-read fails, nor, with <paramref name="keepWhenMissing"/>, when it finds no document.
        /// </remarks>
        private async Task RetireUnreferencedOidcClientSecretAsync(OidcClientRegistration client, string? reference, bool keepWhenMissing)
        {
            if (string.IsNullOrEmpty(reference))
                return;

            OidcClientRegistration? stored;
            try
            {
                stored = await _authenticationRepository.GetOidcClientRegistrationAsync(client.ItemId);
            }
            catch (Exception ex)
            {
                LogOidcClientReReadFailed(client, ex);
                return;
            }

            if (stored == null && keepWhenMissing)
            {
                _logger?.LogWarning("OIDC client {ClientId} could not be read back after the update; its secret is kept.", client.ClientId);
                return;
            }

            if (stored?.ClientSecretReference == reference)
                return;

            await _oidcSecretResolver.RetireAsync(client, reference, CancellationToken.None);
        }

        /// <summary>
        /// The client document was not written after a rotate: a secret created for it is retired
        /// when the stored document does not point at it. A rotation in place cannot be undone (the
        /// store already holds the new value), so it is only logged.
        /// </summary>
        private async Task UndoOidcClientSecretReplaceAsync(OidcClientRegistration client, string? createdReference)
        {
            if (createdReference != null)
            {
                await RetireUnreferencedOidcClientSecretAsync(client, createdReference, keepWhenMissing: true);
                return;
            }

            _logger?.LogWarning("OIDC client {ClientId} was not updated, but its secret rotation cannot be undone.", client.ClientId);
        }

        // Type only: a repository exception message can carry document values.
        private void LogOidcClientReReadFailed(OidcClientRegistration client, Exception ex) =>
            _logger?.LogWarning("OIDC client {ClientId} could not be re-read ({FailureClass}); its secret is kept.", client.ClientId, ex.GetType().Name);

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

            // Supplied secrets go to Blocks Secrets before the insert; the document keeps references only.
            string? clientSecretReference;
            string? privateKeyReference;
            try
            {
                (clientSecretReference, privateKeyReference) =
                    await _secretResolver.StoreAsync(provider, request.ClientSecret, request.PrivateKey);
            }
            catch (SecretVaultException)
            {
                return SecretStoreUnavailable();
            }
            catch (SecretValidationException)
            {
                // With both values supplied the batch is all-or-nothing and does not say which
                // value was refused, so both fields are reported.
                var fields = new List<string>(2);
                if (!string.IsNullOrWhiteSpace(request.ClientSecret)) fields.Add(nameof(IdentityProvider.ClientSecret));
                if (!string.IsNullOrWhiteSpace(request.PrivateKey)) fields.Add(nameof(IdentityProvider.PrivateKey));
                return SecretValueRejected(fields.ToArray());
            }

            if (clientSecretReference != null)
            {
                provider.ClientSecretReference = clientSecretReference;
                provider.ClientSecret = string.Empty;
            }

            if (privateKeyReference != null)
            {
                provider.PrivateKeyReference = privateKeyReference;
                provider.PrivateKey = null;
            }

            try
            {
                await _authenticationRepository.CreateIdentityProviderAsync(provider);
            }
            catch
            {
                // The insert may still have committed (a timeout after the write), so the secrets
                // created above are retired only when no stored document points at them. A missing
                // document means the insert did not land: retire them, then fail as before.
                var created = new List<(IdentityProviderSecretKind Kind, string Reference)>(2);
                if (clientSecretReference != null) created.Add((IdentityProviderSecretKind.ClientSecret, clientSecretReference));
                if (privateKeyReference != null) created.Add((IdentityProviderSecretKind.PrivateKey, privateKeyReference));
                await RetireUnreferencedCreatedSecretsAsync(provider, created, keepWhenMissing: false);
                throw;
            }

            return new BaseResponse { IsSuccess = true };
        }

        private static BaseResponse SecretStoreUnavailable() => new()
        {
            IsSuccess = false,
            Errors = new Dictionary<string, string> { { ErrorSecretStoreUnavailable, SecretStoreUnavailableMessage } }
        };

        private static BaseResponse SecretValueRejected(params string[] fields) => new()
        {
            IsSuccess = false,
            Errors = fields.ToDictionary(f => f, _ => SecretValueRejectedMessage)
        };

        /// <summary>
        /// Retires the secrets of a provider whose document delete was just requested. Data-access
        /// denials make the delete a silent no-op, so the provider is re-read first: the secrets of
        /// a provider that still exists are never retired.
        /// </summary>
        private async Task RetireSecretsOfDeletedProviderAsync(IdentityProvider provider)
        {
            if (string.IsNullOrEmpty(provider.ClientSecretReference) && string.IsNullOrEmpty(provider.PrivateKeyReference))
                return;

            IdentityProvider? stored;
            try
            {
                stored = await _authenticationRepository.GetIdentityProviderByIdAsync(provider.ItemId);
            }
            catch (Exception ex)
            {
                // The delete itself completed: a failing check must not turn it into an error.
                LogReReadFailed(provider, ex);
                return;
            }

            if (stored != null)
            {
                _logger?.LogWarning("Identity provider {Provider} was not deleted; its secrets are kept.", IdentityProviderLog.Name(provider));
                return;
            }

            if (!await IsLiveOidcClientSecretAsync(provider, provider.ClientSecretReference))
                await _secretResolver.RetireAsync(provider, IdentityProviderSecretKind.ClientSecret, provider.ClientSecretReference, CancellationToken.None);
            await _secretResolver.RetireAsync(provider, IdentityProviderSecretKind.PrivateKey, provider.PrivateKeyReference, CancellationToken.None);
        }

        /// <summary>
        /// True when <paramref name="reference"/> is the secret of the OIDC client this provider was
        /// registered from ("register as identity provider"): the two share one secret, which belongs
        /// to the client and is retired with it, never through the provider.
        /// </summary>
        /// <remarks>
        /// Only Blocks-OIDC providers can be linked. When the client cannot be read the secret is
        /// kept (orphan over dangling).
        /// </remarks>
        private async Task<bool> IsLiveOidcClientSecretAsync(IdentityProvider provider, string? reference)
        {
            if (string.IsNullOrEmpty(reference)
                || string.IsNullOrWhiteSpace(provider.ClientId)
                || !string.Equals(provider.ProviderType, IdpConstants.BlocksOidcProviderType, StringComparison.OrdinalIgnoreCase))
                return false;

            OidcClientRegistration? client;
            try
            {
                client = await _authenticationRepository.GetOidcClientRegistrationAsync(provider.ClientId);
            }
            catch (Exception ex)
            {
                LogReReadFailed(provider, ex);
                return true;
            }

            return client?.ClientSecretReference == reference;
        }

        /// <summary>
        /// Retires a secret that an update has just un-referenced, once the stored provider is
        /// confirmed to no longer point at it (a denied update is a silent no-op).
        /// </summary>
        private async Task RetireReplacedSecretAsync(IdentityProvider provider, IdentityProviderSecretKind kind, string? reference)
        {
            if (string.IsNullOrEmpty(reference))
                return;

            IdentityProvider? stored;
            try
            {
                stored = await _authenticationRepository.GetIdentityProviderByIdAsync(provider.ItemId);
            }
            catch (Exception ex)
            {
                // The update itself completed: a failing check must not turn it into an error.
                LogReReadFailed(provider, ex);
                return;
            }

            if (StoredReference(stored, kind) == reference)
            {
                _logger?.LogWarning("Identity provider {Provider} was not updated; its {SecretKind} secret is kept.", IdentityProviderLog.Name(provider), kind);
                return;
            }

            if (kind == IdentityProviderSecretKind.ClientSecret && await IsLiveOidcClientSecretAsync(provider, reference))
                return;

            await _secretResolver.RetireAsync(provider, kind, reference, CancellationToken.None);
        }

        private static string? StoredReference(IdentityProvider? provider, IdentityProviderSecretKind kind) =>
            kind == IdentityProviderSecretKind.ClientSecret ? provider?.ClientSecretReference : provider?.PrivateKeyReference;

        /// <summary>
        /// After a provider write whose outcome is not known (it threw, or a data-access denial made
        /// it a silent no-op), re-reads the provider and retires each secret created in this request
        /// that the stored document does not point at. Returns true when the stored document was
        /// found without one of them, which means the write did not land.
        /// </summary>
        /// <remarks>
        /// Fail safe, orphan over dangling: a soft-deleted secret that a live provider points at
        /// breaks its sign-in, while an orphaned secret is harmless. So nothing is retired when the
        /// re-read fails, nor, with <paramref name="keepWhenMissing"/>, when it finds no document
        /// (after an update, a read rule on a field the update changed can hide the document).
        /// </remarks>
        private async Task<bool> RetireUnreferencedCreatedSecretsAsync(
            IdentityProvider provider, IReadOnlyList<(IdentityProviderSecretKind Kind, string Reference)> created, bool keepWhenMissing)
        {
            if (created.Count == 0)
                return false;

            IdentityProvider? stored;
            try
            {
                stored = await _authenticationRepository.GetIdentityProviderByIdAsync(provider.ItemId);
            }
            catch (Exception ex)
            {
                LogReReadFailed(provider, ex);
                return false;
            }

            if (stored == null && keepWhenMissing)
            {
                _logger?.LogWarning("Identity provider {Provider} could not be read back after the update; the secrets created by this request are kept.", IdentityProviderLog.Name(provider));
                return false;
            }

            var notWritten = false;
            foreach (var (kind, reference) in created)
            {
                if (StoredReference(stored, kind) == reference)
                    continue;

                notWritten = true;
                await _secretResolver.RetireAsync(provider, kind, reference, CancellationToken.None);
            }

            return notWritten;
        }

        // Type only: a repository exception message can carry document values.
        private void LogReReadFailed(IdentityProvider provider, Exception ex) =>
            _logger?.LogWarning("Identity provider {Provider} could not be re-read ({FailureClass}); its secrets are kept.", IdentityProviderLog.Name(provider), ex.GetType().Name);

        private void LogRotationNotUndone(IdentityProvider provider) =>
            _logger?.LogWarning("Identity provider {Provider} was not updated, but a secret rotation in the same request cannot be undone.", IdentityProviderLog.Name(provider));

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
            if (client == null) return;
            client.ClientSecret = null;
            client.ClientSecretReference = null;
        }

        private static void HideSecret(ClientCredential credential)
        {
            credential.ClientSecret = null;
            credential.ClientSecretReference = null;
        }

        private static void HideSecret(IdentityProvider? provider)
        {
            if (provider == null) return;
            provider.ClientSecret = string.Empty;
            provider.PrivateKey = null;
            provider.ClientSecretReference = null;
            provider.PrivateKeyReference = null;
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

            // Supplied secrets are rotated or stored in Blocks Secrets; an empty one changes nothing.
            var secretWrite = await WriteSuppliedSecretsAsync(existing, request);
            if (secretWrite.Error != null)
                return secretWrite.Error;

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
            if (request.AppleAudience != null) existing.AppleAudience = request.AppleAudience;

            var blocksContext = BlocksContext.GetContext();
            existing.LastUpdatedBy = blocksContext?.UserId;
            existing.LastUpdatedDate = DateTime.UtcNow;

            try
            {
                await _authenticationRepository.UpdateIdentityProviderAsync(existing);
            }
            catch
            {
                // The replace may still have committed (a timeout after the write): a secret created
                // here is retired only when the stored document does not point at it.
                await RetireUnreferencedCreatedSecretsAsync(existing, secretWrite.Created, keepWhenMissing: true);
                if (secretWrite.Rotated)
                    LogRotationNotUndone(existing);
                throw;
            }

            // A denied write is a silent no-op: a secret created here that the stored document
            // does not point at would be an orphan, so it is retired. The response stays as today.
            // A rotation in the same request cannot be undone, so it is logged. A denied update that
            // only rotated cannot be told apart from an applied one: its reference is unchanged.
            if (await RetireUnreferencedCreatedSecretsAsync(existing, secretWrite.Created, keepWhenMissing: true) && secretWrite.Rotated)
                LogRotationNotUndone(existing);

            return new BaseResponse { IsSuccess = true };
        }

        private sealed record SecretWriteResult(BaseResponse? Error, List<(IdentityProviderSecretKind Kind, string Reference)> Created, bool Rotated);

        private async Task<SecretWriteResult> WriteSuppliedSecretsAsync(IdentityProvider existing, UpdateIdentityProviderRequest request)
        {
            var created = new List<(IdentityProviderSecretKind Kind, string Reference)>(2);
            var rotated = false;
            var current = IdentityProviderSecretKind.ClientSecret;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.ClientSecret))
                {
                    current = IdentityProviderSecretKind.ClientSecret;
                    var (reference, wasCreated) = await _secretResolver.ReplaceAsync(existing, current, request.ClientSecret);
                    if (wasCreated) created.Add((current, reference)); else rotated = true;
                    existing.ClientSecretReference = reference;
                    existing.ClientSecret = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(request.PrivateKey))
                {
                    current = IdentityProviderSecretKind.PrivateKey;
                    var (reference, wasCreated) = await _secretResolver.ReplaceAsync(existing, current, request.PrivateKey);
                    if (wasCreated) created.Add((current, reference)); else rotated = true;
                    existing.PrivateKeyReference = reference;
                    existing.PrivateKey = null;
                }
            }
            catch (SecretVaultException)
            {
                await UndoSecretWritesAsync(existing, new SecretWriteResult(null, created, rotated));
                return new SecretWriteResult(SecretStoreUnavailable(), [], false);
            }
            catch (SecretValidationException)
            {
                await UndoSecretWritesAsync(existing, new SecretWriteResult(null, created, rotated));
                return new SecretWriteResult(SecretValueRejected(current.ToString()), [], false);
            }
            catch
            {
                // Any other failure propagates to the global handler as before, leaving no new orphan.
                await UndoSecretWritesAsync(existing, new SecretWriteResult(null, created, rotated));
                throw;
            }

            return new SecretWriteResult(null, created, rotated);
        }

        /// <summary>
        /// The provider document was not written: retire secrets this call created. A rotation
        /// cannot be undone, so it is only logged (the stored reference now holds the new value).
        /// </summary>
        private async Task UndoSecretWritesAsync(IdentityProvider provider, SecretWriteResult write)
        {
            foreach (var (kind, reference) in write.Created)
                await _secretResolver.RetireAsync(provider, kind, reference, CancellationToken.None);

            if (write.Rotated)
                LogRotationNotUndone(provider);
        }

        public async Task<BaseResponse> DeleteIdentityProviderAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "id_required", "Provider ID is required." } } };

            var existing = await _authenticationRepository.GetIdentityProviderByIdAsync(id);
            if (existing == null)
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { ErrorNotFound, "Provider not found." } } };

            // Delete the IdentityProvider, then its secrets (a live provider never points at a deleted secret)
            await _authenticationRepository.DeleteIdentityProviderAsync(id);
            await RetireSecretsOfDeletedProviderAsync(existing);

            // Delete related OidcClientRegistration by ClientId
            if (!string.IsNullOrWhiteSpace(existing.ClientId))
            {
                var relatedCredential = await _authenticationRepository.GetOidcClientRegistrationAsync(existing.ClientId);
                if (relatedCredential != null)
                {
                    var deleteRequest = new DeleteOIDCClientRequest { ItemId = existing.ClientId };
                    // Call repository directly to avoid recursive sync logic
                    await _authenticationRepository.DeleteOidcClientAsync(deleteRequest);
                    await RetireSecretOfDeletedOidcClientAsync(relatedCredential);
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

            // Rotated in place when the client already has a secret in the store; a client still on
            // plaintext gets a new secret, which moves it to the store.
            var clientSecret = GenerateClientSecret();
            string reference;
            bool created;
            try
            {
                (reference, created) = await _oidcSecretResolver.ReplaceAsync(credential, clientSecret);
            }
            catch (SecretVaultException)
            {
                return new RotateOidcClientSecretResponse { IsSuccess = false, Errors = SecretStoreUnavailable().Errors };
            }

            var createdReference = created ? reference : null;
            credential.ClientSecretReference = reference;
            credential.ClientSecret = string.Empty;
            credential.LastUpdatedBy = blocksContext?.UserId;
            credential.LastUpdatedDate = rotatedAt;

            try
            {
                await _authenticationRepository.SaveOidcClientRegistrationAsync(credential);
            }
            catch
            {
                await UndoOidcClientSecretReplaceAsync(credential, createdReference);
                throw;
            }

            // The linked Blocks IdentityProvider shares the client's secret reference, so a rotation
            // in place reaches it with no write. It is updated only when it points elsewhere: the
            // rotation stored a new secret, or it still holds a plaintext copy from before Blocks
            // Secrets. Same ClientId link the RegisterAsIdentityProvider sync in SaveOIDCClientAsync uses.
            if (credential.RegisterAsIdentityProvider && !string.IsNullOrWhiteSpace(credential.ClientId))
            {
                var linkedProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(credential.ClientId);
                if (linkedProvider != null && !string.IsNullOrWhiteSpace(linkedProvider.ItemId)
                    && (linkedProvider.ClientSecretReference != reference || !string.IsNullOrEmpty(linkedProvider.ClientSecret)))
                {
                    var replacedReference = linkedProvider.ClientSecretReference;
                    linkedProvider.ClientSecret = string.Empty;
                    linkedProvider.ClientSecretReference = reference;
                    await _authenticationRepository.UpdateIdentityProviderAsync(linkedProvider);
                    if (replacedReference != reference)
                        await RetireReplacedSecretAsync(linkedProvider, IdentityProviderSecretKind.ClientSecret, replacedReference);
                }
            }

            await NotifyClientCredentialChangedAsync(OidcClientKind, "secret-rotated", credential.ClientId, credential.CreatedBy, null);

            return new RotateOidcClientSecretResponse
            {
                IsSuccess = true,
                ItemId = credential.ItemId,
                ClientId = credential.ClientId,
                ClientSecret = clientSecret,
                RotatedAt = rotatedAt,
                RotatedBy = blocksContext?.UserId,
            };
        }
    }
}
