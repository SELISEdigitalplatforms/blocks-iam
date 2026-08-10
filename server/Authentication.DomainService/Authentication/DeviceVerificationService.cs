using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

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
        private readonly IOptions<DeviceFlowOptions> _options;
        private readonly ILogger<DeviceVerificationService> _logger;

        public DeviceVerificationService(
            IDeviceAuthorizationRepository repository,
            IIdpSessionRepository sessionRepository,
            IAuthenticationRepository authenticationRepository,
            IOptions<DeviceFlowOptions> options,
            ILogger<DeviceVerificationService> logger)
        {
            _repository = repository;
            _sessionRepository = sessionRepository;
            _authenticationRepository = authenticationRepository;
            _options = options;
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

            if (!RequestTenantMatches(httpContext.Request, entity.TenantId))
            {
                _logger.LogWarning("Device verify tenant mismatch for request {RequestId}", entity.Id);
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
            var apiBase = OidcRedirectUrlBuilder.ResolvePublicBaseUrl(httpContext.Request, _options.Value.PublicBaseUrl);
            var currentUrl = $"{apiBase}/device/{Uri.EscapeDataString(tenantId)}?user_code={Uri.EscapeDataString(entity.UserCode)}";

            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenantId)];
            var session = !string.IsNullOrWhiteSpace(sessionId)
                ? await _sessionRepository.GetBySessionIdAsync(sessionId)
                : null;

            var sessionValid = IsValidTenantSession(session, tenantId);

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
            var approvalToken = CreateApprovalToken();
            var approvalTokenHash = HashApprovalToken(approvalToken);
            var tokenStored = await _repository.SetApprovalTokenHashAsync(entity.Id, approvalTokenHash, ct);
            if (!tokenStored)
            {
                return new ObjectResult(new { error = "request_not_pending", error_description = "device authorization request is no longer pending" })
                {
                    StatusCode = StatusCodes.Status410Gone
                };
            }

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
                    RequestIpAddress = entity.IpAddress,
                    RequestUserAgent = entity.UserAgent,
                    DeviceName = entity.DeviceName,
                    DeviceInfo = entity.DeviceInfo,
                    ApprovalToken = approvalToken,
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

            if (!RequestTenantMatches(httpContext.Request, entity.TenantId))
            {
                _logger.LogWarning("Device decision tenant mismatch for request {RequestId}", entity.Id);
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

            if (string.IsNullOrWhiteSpace(request.ApprovalToken)
                || !ApprovalTokenMatches(request.ApprovalToken, entity.ApprovalTokenHash))
            {
                return DeviceEndpointErrors.InvalidRequest("approval token is invalid");
            }

            var tenantId = entity.TenantId;
            var apiBase = OidcRedirectUrlBuilder.ResolvePublicBaseUrl(httpContext.Request, _options.Value.PublicBaseUrl);
            var sessionId = httpContext.Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenantId)];
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new ObjectResult(new { error = "login_required" }) { StatusCode = StatusCodes.Status401Unauthorized };
            }

            var session = await _sessionRepository.GetBySessionIdAsync(sessionId);
            if (!IsValidTenantSession(session, tenantId))
            {
                return new ObjectResult(new { error = "login_required" }) { StatusCode = StatusCodes.Status401Unauthorized };
            }

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
                // Resolve the same effective org a normal authorization_code login would use
                // (OidcAuthorizationEndpoint), so a device-flow token carries the org the user
                // actually has access to instead of minting with no OrganizationId at all.
                var approvingUser = await _authenticationRepository.GetUserByIdAsync(approver.UserId);
                var organizationId = approvingUser != null
                    ? OrganizationAccessResolver.ResolveEffectiveOrganizationId(approvingUser)
                    : null;

                ok = await _repository.MarkApprovedAsync(entity.Id, approver.UserId, now, ct, organizationId);
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

        private static bool IsValidTenantSession(IdpSessionModel? session, string tenantId)
        {
            return session != null
                && !session.RevokedAt.HasValue
                && !session.IsExpired()
                && session.Accounts.Any(a => string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RequestTenantMatches(HttpRequest request, string tenantId)
        {
            var presentedTenant = ResolvePresentedTenant(request);
            return string.IsNullOrWhiteSpace(presentedTenant)
                || string.Equals(presentedTenant, tenantId, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolvePresentedTenant(HttpRequest request)
        {
            if (request.Headers.TryGetValue("X-Blocks-Key", out var headerTenant)
                && !string.IsNullOrWhiteSpace(headerTenant.ToString()))
            {
                return headerTenant.ToString();
            }

            var queryTenant = request.Query["tenant_id"].ToString();
            return string.IsNullOrWhiteSpace(queryTenant) ? null : queryTenant;
        }

        private static string CreateApprovalToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Base64UrlEncode(bytes);
        }

        private static string HashApprovalToken(string token)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(token);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static bool ApprovalTokenMatches(string token, string? expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                return false;
            }

            var actualHash = HashApprovalToken(token);
            var actualBytes = System.Text.Encoding.UTF8.GetBytes(actualHash);
            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedHash);
            return actualBytes.Length == expectedBytes.Length
                && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
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
