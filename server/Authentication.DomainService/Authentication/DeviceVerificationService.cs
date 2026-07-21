using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Repositories;
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
    /// <item><c>POST /api/device/verify</c>   &mdash; validate <c>user_code</c>, return either a
    /// consent payload (<c>status: "ready"</c>) or a login URL (<c>status: "login_required"</c>).</item>
    /// <item><c>POST /api/device/decision</c> &mdash; set <c>Approved</c> / <c>Denied</c> and redirect
    /// the SPA to the success page.</item>
    /// </list>
    /// The IdP session cookie is keyed on the tenant
    /// (<see cref="IdpConstants.BuildIdpSessionCookieKey"/>).
    /// </summary>
    public sealed class DeviceVerificationService
    {
        private readonly IDeviceAuthorizationRepository _repository;
        private readonly IIdpSessionRepository _sessionRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<DeviceVerificationService> _logger;

        public DeviceVerificationService(
            IDeviceAuthorizationRepository repository,
            IIdpSessionRepository sessionRepository,
            IAuthenticationRepository authenticationRepository,
            ILogger<DeviceVerificationService> logger)
        {
            _repository = repository;
            _sessionRepository = sessionRepository;
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        /// <summary>
        /// Validates a <c>user_code</c>, looks up the underlying entity, and either returns the
        /// inline consent payload (when an IdP session is bound to the tenant) or a login URL for
        /// the SPA to follow. Replaces the previous BeginAsync + ContinueAsync pair.
        /// </summary>
        public async Task<IActionResult> VerifyAsync(DeviceVerifyRequest request, HttpContext httpContext, CancellationToken ct = default)
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
            var apiBase = $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
            var currentUrl = $"{apiBase}/device/{Uri.EscapeDataString(tenantId)}?user_code={Uri.EscapeDataString(entity.UserCode)}";

            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenantId)];
            var session = !string.IsNullOrWhiteSpace(sessionId)
                ? await _sessionRepository.GetBySessionIdAsync(sessionId)
                : null;

            var sessionValid = session != null
                && !session.RevokedAt.HasValue
                && !session.IsExpired()
                && session.Accounts.Any(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

            if (!sessionValid)
            {
                var loginRedirect = OidcRedirectUrlBuilder.BuildRedirectUri(
                    "/oidc/login",
                    new Dictionary<string, string>
                    {
                        ["client_id"] = entity.ClientId,
                        ["scope"] = entity.RequestedScopes,
                        ["tenant_id"] = tenantId,
                        ["returnUrl"] = currentUrl,
                    });

                return new OkObjectResult(new DeviceVerifyResponse
                {
                    Status = DeviceVerifyStatus.LoginRequired,
                    ReturnUrl = loginRedirect,
                });
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(entity.ClientId);

            return new OkObjectResult(new DeviceVerifyResponse
            {
                Status = DeviceVerifyStatus.Ready,
                Payload = new DeviceConsentPayload
                {
                    ClientName = client?.ClientName ?? entity.ClientId,
                    ClientId = entity.ClientId,
                    Scopes = (entity.RequestedScopes ?? string.Empty)
                        .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                    Tenant = tenantId,
                    UserCode = entity.UserCode,
                },
            });
        }

        /// <summary>
        /// Records an allow/deny decision against the device authorization request identified by
        /// <c>user_code</c>. Replaces the previous ApproveAsync; the <c>interactionId</c>
        /// indirection has been removed.
        /// </summary>
        public async Task<IActionResult> DecisionAsync(DeviceDecisionRequest request, HttpContext httpContext, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.UserCode))
            {
                return DeviceEndpointErrors.InvalidRequest("user_code is required");
            }

            var normalizedCode = NormalizeUserCode(request.UserCode);
            var entity = await _repository.GetByUserCodeAsync(normalizedCode, ct);
            if (entity == null)
            {
                return new ObjectResult(new { error = "invalid_grant", error_description = "user_code not found" })
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
            }

            var decision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "allow" && decision != "deny")
            {
                return DeviceEndpointErrors.InvalidRequest("decision must be 'allow' or 'deny'");
            }

            if (entity.Status == DeviceAuthorizationStatus.Expired
                || entity.ExpiresAt <= DateTime.UtcNow)
            {
                return DeviceEndpointErrors.ExpiredToken("user_code has expired");
            }

            if (entity.Status != DeviceAuthorizationStatus.Pending)
            {
                return new ObjectResult(new { error = "request_not_pending", error_description = "device authorization request is no longer pending" })
                {
                    StatusCode = StatusCodes.Status410Gone
                };
            }

            var tenantId = entity.TenantId;
            var apiBase = $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
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

            if (!ok)
            {
                _logger.LogWarning("Device decide CAS failed for {RequestId} (already terminal)", entity.Id);
                return new ObjectResult(new { error = "request_not_pending" }) { StatusCode = StatusCodes.Status410Gone };
            }

            return new OkObjectResult(new DeviceApproveResponse
            {
                Redirect = $"{apiBase}/device/{Uri.EscapeDataString(tenantId)}/success?outcome={(newStatus == DeviceAuthorizationStatus.Approved ? "approved" : "denied")}",
                Status = newStatus,
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
