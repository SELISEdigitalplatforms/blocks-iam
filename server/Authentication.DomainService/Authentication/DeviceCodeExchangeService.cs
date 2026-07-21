using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Handles the RFC 8628 §3.4 <c>urn:ietf:params:oauth:grant-type:device_code</c> grant.
    /// Dispatched from <see cref="OidcTokenEndpoint"/> when the token endpoint sees that grant type.
    /// Returns the standard RFC 6749 token JSON on success, or one of:
    /// <c>authorization_pending</c>, <c>slow_down</c>, <c>access_denied</c>, <c>expired_token</c>,
    /// <c>invalid_grant</c>.
    /// </summary>
    public sealed class DeviceCodeExchangeService
    {
        private readonly IDeviceAuthorizationRepository _repository;
        private readonly DeviceCodeGenerator _codeGenerator;
        private readonly IOidcTokenMintService _mintService;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<DeviceCodeExchangeService> _logger;

        public DeviceCodeExchangeService(
            IDeviceAuthorizationRepository repository,
            DeviceCodeGenerator codeGenerator,
            IOidcTokenMintService mintService,
            IAuthenticationRepository authenticationRepository,
            IUserRepository userRepository,
            ILogger<DeviceCodeExchangeService> logger)
        {
            _repository = repository;
            _codeGenerator = codeGenerator;
            _mintService = mintService;
            _authenticationRepository = authenticationRepository;
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<IActionResult> ExchangeAsync(HttpRequest request, CancellationToken ct = default)
        {
            var deviceCode = request.Form["device_code"].ToString();
            var clientId = request.Form["client_id"].ToString();

            if (string.IsNullOrWhiteSpace(clientId))
            {
                OidcRedirectUrlBuilder.TryReadBasicClientAuthentication(request, out clientId, out _);
            }

            if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(clientId))
            {
                return DeviceEndpointErrors.InvalidGrant("device_code and client_id are required");
            }

            var hash = _codeGenerator.HashDeviceCode(deviceCode);
            var entity = await _repository.GetByDeviceCodeHashAsync(hash, ct);
            if (entity == null)
            {
                return DeviceEndpointErrors.InvalidGrant("device_code is invalid");
            }

            if (!string.Equals(entity.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Device code client mismatch: presented={Presented} actual={Actual}", clientId, entity.ClientId);
                return DeviceEndpointErrors.InvalidGrant("client_id does not match device_code");
            }

            var cookieTenantId = ResolveTenantIdFromRequest(request);
            if (!string.IsNullOrWhiteSpace(cookieTenantId)
                && !string.Equals(cookieTenantId, entity.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Device code tenant mismatch: presented={Presented} actual={Actual}", cookieTenantId, entity.TenantId);
                return DeviceEndpointErrors.InvalidGrant("tenant mismatch");
            }

            switch (entity.Status)
            {
                case DeviceAuthorizationStatus.Denied:
                    return DeviceEndpointErrors.AccessDenied("user denied the request");

                case DeviceAuthorizationStatus.Expired:
                    return DeviceEndpointErrors.ExpiredToken("device_code has expired");

                case DeviceAuthorizationStatus.Consumed:
                    return DeviceEndpointErrors.AccessDenied("device_code already used");

                case DeviceAuthorizationStatus.Pending:
                    return await HandlePendingAsync(entity, ct);

                case DeviceAuthorizationStatus.Approved:
                    return await HandleApprovedAsync(entity, request, ct);

                default:
                    return DeviceEndpointErrors.InvalidGrant("unsupported device_code status");
            }
        }

        private async Task<IActionResult> HandlePendingAsync(DeviceAuthorizationRequestModel entity, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (entity.ExpiresAt <= now)
            {
                return DeviceEndpointErrors.ExpiredToken("device_code has expired");
            }

            var elapsedSinceLastPoll = now - entity.LastPollAt;
            if (elapsedSinceLastPoll.TotalSeconds < Math.Max(entity.PollIntervalSeconds - 1, 0))
            {
                var newInterval = await _repository.BumpPollIntervalAsync(entity.Id, entity.PollIntervalSeconds, ct);
                return DeviceEndpointErrors.SlowDown($"polling too frequently; interval is now {newInterval}s", newInterval);
            }

            await _repository.UpdatePollAsync(entity.Id, now, entity.PollsObserved + 1, ct);
            return DeviceEndpointErrors.AuthorizationPending("user has not yet approved the request");
        }

        private async Task<IActionResult> HandleApprovedAsync(DeviceAuthorizationRequestModel entity, HttpRequest httpRequest, CancellationToken ct)
        {
            if (entity.ExpiresAt <= DateTime.UtcNow)
            {
                return DeviceEndpointErrors.ExpiredToken("device_code has expired");
            }

            if (string.IsNullOrWhiteSpace(entity.UserId))
            {
                _logger.LogWarning("Device authorization request {Id} is Approved without a UserId", entity.Id);
                return DeviceEndpointErrors.AccessDenied("approved without user binding");
            }

            var user = await _userRepository.GetUserByIdAsync(entity.UserId);
            if (user == null)
            {
                return DeviceEndpointErrors.AccessDenied("user not found");
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return new ObjectResult(new { error = "account_locked", error_description = "Account is temporarily locked" })
                {
                    StatusCode = StatusCodes.Status423Locked
                };
            }

            var consumed = await _repository.MarkConsumedAsync(entity.Id, DateTime.UtcNow, ct);
            if (!consumed)
            {
                _logger.LogWarning("Device authorization request {Id} CAS Approved->Consumed failed", entity.Id);
                return DeviceEndpointErrors.AccessDenied("device_code already used");
            }

            var mintResult = await _mintService.MintAsync(new OidcTokenMintRequest
            {
                User = user,
                TenantId = entity.TenantId,
                Scope = entity.RequestedScopes,
                ClientId = entity.ClientId,
                Amr = new List<string> { "device_code" },
                Request = httpRequest
            }, ct);

            var includeRefreshToken = ContainsOfflineAccess(entity.RequestedScopes);

            var response = new Dictionary<string, object?>
            {
                ["access_token"] = mintResult.AccessToken,
                ["id_token"] = mintResult.IdToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = mintResult.ExpiresIn,
                ["scope"] = mintResult.Scope
            };
            if (includeRefreshToken && !string.IsNullOrWhiteSpace(mintResult.RefreshToken))
            {
                response["refresh_token"] = mintResult.RefreshToken;
            }

            return new OkObjectResult(response);
        }

        private static bool ContainsOfflineAccess(string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return false;
            }
            return scope
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(s => string.Equals(s, "offline_access", StringComparison.OrdinalIgnoreCase));
        }

        private static string? ResolveTenantIdFromRequest(HttpRequest request)
        {
            var formTenant = request.Form["tenant_id"].ToString();
            if (!string.IsNullOrWhiteSpace(formTenant))
            {
                return formTenant;
            }
            var queryTenant = request.Query["tenant_id"].ToString();
            if (!string.IsNullOrWhiteSpace(queryTenant))
            {
                return queryTenant;
            }
            if (request.Headers.TryGetValue("X-Blocks-Key", out var hv))
            {
                return hv.ToString();
            }
            return null;
        }
    }
}