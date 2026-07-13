using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Orchestrator for the browser-facing portion of RFC 8628 device flow:
    /// <list type="bullet">
    /// <item><c>POST /device</c>            — validate <c>user_code</c>, mint <c>interactionId</c>,
    /// redirect through OIDC login.</item>
    /// <item><c>GET  /continue/{id}</c> — return consent payload (clientName, scopes, tenant).</item>
    /// <item><c>POST /approve</c>    — set Approved/Denied; navigate to /device/success.</item>
    /// </list>
    /// The IdP session cookie is keyed on the tenant (<see cref="IdpConstants.BuildIdpSessionCookieKey"/>).
    /// </summary>
    public sealed class DeviceVerificationController
    {
        private readonly IDeviceAuthorizationRepository _repository;
        private readonly IDeviceInteractionStateStore _interactionStore;
        private readonly IIdpSessionRepository _sessionRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly ILogger<DeviceVerificationController> _logger;

        public DeviceVerificationController(
            IDeviceAuthorizationRepository repository,
            IDeviceInteractionStateStore interactionStore,
            IIdpSessionRepository sessionRepository,
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            ILogger<DeviceVerificationController> logger)
        {
            _repository = repository;
            _interactionStore = interactionStore;
            _sessionRepository = sessionRepository;
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<IActionResult> BeginAsync(DeviceInteractionRequest request, HttpContext httpContext, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.UserCode))
            {
                return DeviceEndpointErrors.InvalidRequest("user_code is required");
            }

            var normalizedCode = NormalizeUserCode(request.UserCode);
            var entity = await _repository.GetByUserCodeAsync(normalizedCode, ct);
            if (entity == null)
            {
                return DeviceEndpointErrors.InvalidGrant("user_code not found");
            }

            if (entity.Status == DeviceAuthorizationStatus.Expired
                || entity.ExpiresAt <= DateTime.UtcNow)
            {
                return DeviceEndpointErrors.ExpiredToken("user_code has expired");
            }

            if (entity.Status != DeviceAuthorizationStatus.Pending)
            {
                return DeviceEndpointErrors.InvalidGrant("user_code is no longer pending");
            }

            var tenantId = entity.TenantId;
            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenantId)];
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var session = await _sessionRepository.GetBySessionIdAsync(sessionId);
                if (session != null
                    && !session.RevokedAt.HasValue
                    && !session.IsExpired()
                    && session.Accounts.Any(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)))
                {
                    return new OkObjectResult(new DeviceInteractionResponse
                    {
                        Redirect = $"/device/{Uri.EscapeDataString(tenantId)}/continue/{Uri.EscapeDataString(entity.Id)}",
                        InteractionId = entity.Id
                    });
                }
            }

            var interactionId = Guid.NewGuid().ToString("n");
            var context = new DeviceInteractionContext
            {
                RequestId = entity.Id,
                TenantId = tenantId,
                ClientId = entity.ClientId,
                CreatedAt = DateTime.UtcNow
            };

            var ttl = (entity.ExpiresAt - DateTime.UtcNow) + TimeSpan.FromMinutes(10);
            if (ttl < TimeSpan.FromMinutes(1))
            {
                ttl = TimeSpan.FromMinutes(10);
            }
            await _interactionStore.SaveAsync(interactionId, context, ttl, ct);

            var returnUrl = $"/device/{Uri.EscapeDataString(tenantId)}/continue/{Uri.EscapeDataString(interactionId)}";
            var loginRedirect = $"/oidc/login?returnUrl={Uri.EscapeDataString(returnUrl)}&tenant_id={Uri.EscapeDataString(tenantId)}";

            return new OkObjectResult(new DeviceInteractionResponse
            {
                Redirect = loginRedirect,
                InteractionId = interactionId
            });
        }

        public async Task<IActionResult> ContinueAsync(string interactionId, HttpContext httpContext, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "interactionId required" });
            }

            var context = await _interactionStore.GetAsync(interactionId, ct);
            if (context == null)
            {
                return new ObjectResult(new { error = "interaction_expired", error_description = "device interaction no longer valid" })
                {
                    StatusCode = StatusCodes.Status410Gone
                };
            }

            var entity = await _repository.GetByIdAsync(context.RequestId, ct);
            if (entity == null || entity.Status != DeviceAuthorizationStatus.Pending)
            {
                return new ObjectResult(new { error = "request_not_pending", error_description = "device authorization request is no longer pending" })
                {
                    StatusCode = StatusCodes.Status410Gone
                };
            }

            var tenantId = context.TenantId;
            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenantId)];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new ObjectResult(new { error = "login_required", error_description = "user must authenticate first" })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }

            var session = await _sessionRepository.GetBySessionIdAsync(sessionId);
            if (session == null
                || session.RevokedAt.HasValue
                || session.IsExpired()
                || !session.Accounts.Any(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)))
            {
                return new ObjectResult(new { error = "login_required", error_description = "user must authenticate first" })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(context.ClientId);

            return new OkObjectResult(new DeviceConsentPayload
            {
                ClientName = client?.ClientName ?? context.ClientId,
                ClientId = context.ClientId,
                Scopes = (entity.RequestedScopes ?? string.Empty)
                    .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                Tenant = context.TenantId,
                UserCode = entity.UserCode
            });
        }

        public async Task<IActionResult> ApproveAsync(DeviceApproveRequest request, HttpContext httpContext, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.InteractionId))
            {
                return DeviceEndpointErrors.InvalidRequest("interactionId required");
            }

            var context = await _interactionStore.GetAsync(request.InteractionId, ct);
            if (context == null)
            {
                return new ObjectResult(new { error = "interaction_expired" }) { StatusCode = StatusCodes.Status410Gone };
            }

            var tenantId = context.TenantId;
            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenantId)];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new ObjectResult(new { error = "login_required" }) { StatusCode = StatusCodes.Status401Unauthorized };
            }

            var session = await _sessionRepository.GetBySessionIdAsync(sessionId);
            var approver = session?.Accounts.FirstOrDefault(a =>
                string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
            if (approver == null)
            {
                return new ObjectResult(new { error = "login_required" }) { StatusCode = StatusCodes.Status401Unauthorized };
            }

            var entity = await _repository.GetByIdAsync(context.RequestId, ct);
            if (entity == null || entity.Status != DeviceAuthorizationStatus.Pending)
            {
                return new ObjectResult(new { error = "request_not_pending" }) { StatusCode = StatusCodes.Status410Gone };
            }

            var decision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "allow" && decision != "deny")
            {
                return DeviceEndpointErrors.InvalidRequest("decision must be 'allow' or 'deny'");
            }

            var now = DateTime.UtcNow;
            bool ok;
            string newStatus;
            if (decision == "allow")
            {
                ok = await _repository.MarkApprovedAsync(entity.Id, approver.UserId, now, ct);
                newStatus = DeviceAuthorizationStatus.Approved;
            }
            else
            {
                ok = await _repository.MarkDeniedAsync(entity.Id, now, ct);
                newStatus = DeviceAuthorizationStatus.Denied;
            }

            await _interactionStore.RemoveAsync(request.InteractionId, ct);

            if (!ok)
            {
                _logger.LogWarning("Device approve/deny CAS failed for {RequestId} (already terminal)", entity.Id);
                return new ObjectResult(new { error = "request_not_pending" }) { StatusCode = StatusCodes.Status410Gone };
            }

            return new OkObjectResult(new DeviceApproveResponse
            {
                Redirect = $"/device/{Uri.EscapeDataString(tenantId)}/success",
                Status = newStatus
            });
        }

        public IActionResult EntryAsync(HttpRequest request)
        {
            var acceptsJson = request.Headers.Accept.Any(h =>
                h != null && h!.Contains("application/json", StringComparison.OrdinalIgnoreCase));

            if (acceptsJson)
            {
                return new OkObjectResult(new { redirect = "/device" });
            }

            return new OkObjectResult(new { redirect = "/device" });
        }

        private static string NormalizeUserCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            return input.Replace(" ", string.Empty).Replace("\u2013", "-").ToUpperInvariant();
        }
    }
}