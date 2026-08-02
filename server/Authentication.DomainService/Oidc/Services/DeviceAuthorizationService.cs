using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Iam.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Authentication.DomainService.Oidc.Services
{
    /// <summary>
    /// Handles POST /api/oidc/device_authorization (RFC 8628 section 3.1).
    /// Validates the client (must be <c>IsDeviceFlowClient=true</c> and active), the tenant,
    /// and the requested scope; mints a fresh <c>device_code</c> + <c>user_code</c>; persists
    /// the request; and returns the standard RFC 8628 JSON envelope.
    /// </summary>
    public interface IDeviceAuthorizationService
    {
        Task<DeviceAuthorizationResponse> RequestAsync(DeviceAuthorizationRequest request, HttpRequest httpRequest, CancellationToken ct = default);
    }

    public sealed class DeviceAuthorizationService : IDeviceAuthorizationService
    {
        private readonly IDeviceAuthorizationRepository _repository;
        private readonly DeviceCodeGenerator _codeGenerator;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IOptions<DeviceFlowOptions> _options;
        private readonly ILogger<DeviceAuthorizationService> _logger;

        public DeviceAuthorizationService(
            IDeviceAuthorizationRepository repository,
            DeviceCodeGenerator codeGenerator,
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            IOptions<DeviceFlowOptions> options,
            ILogger<DeviceAuthorizationService> logger)
        {
            _repository = repository;
            _codeGenerator = codeGenerator;
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _options = options;
            _logger = logger;
        }

        public async Task<DeviceAuthorizationResponse> RequestAsync(DeviceAuthorizationRequest request, HttpRequest httpRequest, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                throw new DeviceAuthorizationException("invalid_request", "client_id is required");
            }

            var blocksContext = BlocksContext.GetContext();

            if (string.IsNullOrWhiteSpace(blocksContext.TenantId))
            {
                throw new DeviceAuthorizationException("invalid_request", "tenant_id is required");
            }

            var tenant = _tenants.GetTenantByID(blocksContext.TenantId);
            if (tenant == null)
            {
                throw new DeviceAuthorizationException("invalid_tenant", $"tenant '{blocksContext.TenantId}' not found");
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ClientId);
            if (client == null)
            {
                throw new DeviceAuthorizationException("invalid_client", "client not found");
            }

            if (!OidcClientValidator.IsGrantAllowed(client, GrantTypes.DeviceCode))
            {
                throw new DeviceAuthorizationException("unauthorized_client", "client is not authorized for the device_code grant");
            }

            var resolvedScopes = OidcClientValidator.ValidateScopes(client, request.Scope, ScopeConstants.Supported);
            if (resolvedScopes.Count == 0 && !string.IsNullOrWhiteSpace(request.Scope))
            {
                throw new DeviceAuthorizationException("invalid_scope", "no requested scope is allowed for this client");
            }

            var options = _options.Value;
            DeviceAuthorizationRequestModel entity;
            string deviceCode;
            string userCode;
            for (var attempt = 0; ; attempt++)
            {
                deviceCode = _codeGenerator.GenerateDeviceCode();
                userCode = await GenerateUniqueUserCodeAsync(ct);
                var now = DateTime.UtcNow;

                entity = new DeviceAuthorizationRequestModel
                {
                    DeviceCodeHash = _codeGenerator.HashDeviceCode(deviceCode),
                    UserCode = userCode,
                    ClientId = request.ClientId,
                    TenantId = blocksContext.TenantId,
                    RequestedScopes = string.Join(' ', resolvedScopes),
                    Status = DeviceAuthorizationStatus.Pending,
                    CreatedAt = now,
                    ExpiresAt = now.Add(options.Expiration),
                    LastPollAt = now,
                    PollIntervalSeconds = (int)options.PollInterval.TotalSeconds,
                    IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(httpRequest),
                    UserAgent = httpRequest.Headers.UserAgent.ToString()
                };

                try
                {
                    await _repository.CreateAsync(entity, ct);
                    break;
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey && attempt < 4)
                {
                    _logger.LogWarning(ex, "Device authorization code collision while creating request; retrying.");
                }
            }

            var apiBase = $"{httpRequest.Scheme}://{httpRequest.Host.Value}";
            var verificationUri = OidcRedirectUrlBuilder.BuildVerificationUri(apiBase, null, blocksContext.TenantId);
            var verificationUriComplete = OidcRedirectUrlBuilder.BuildVerificationUriComplete(apiBase, userCode, blocksContext.TenantId);

            _logger.LogInformation("Device authorization request created {Id} for client {ClientId} tenant {TenantId}", entity.Id, request.ClientId, blocksContext.TenantId);

            return new DeviceAuthorizationResponse
            {
                DeviceCode = deviceCode,
                UserCode = userCode,
                VerificationUri = verificationUri,
                VerificationUriComplete = verificationUriComplete,
                ExpiresIn = (int)options.Expiration.TotalSeconds,
                Interval = (int)options.PollInterval.TotalSeconds
            };
        }

        private async Task<string> GenerateUniqueUserCodeAsync(CancellationToken ct)
        {
            const int maxAttempts = 3;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var candidate = _codeGenerator.GenerateUserCode();
                var existing = await _repository.GetByUserCodeAsync(candidate, ct);
                if (existing == null)
                {
                    return candidate;
                }
            }

            _logger.LogWarning("DeviceCodeGenerator: exhausted retries when generating a unique user code; returning last attempt.");
            return _codeGenerator.GenerateUserCode();
        }
    }

    public sealed class DeviceAuthorizationException : Exception
    {
        public string Error { get; }
        public string? ErrorDescription { get; }

        public DeviceAuthorizationException(string error, string? errorDescription = null) : base(errorDescription ?? error)
        {
            Error = error;
            ErrorDescription = errorDescription;
        }
    }
}
