using Authentication.DomainService.OAuth;
using Iam.DomainService.Utilities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Dtos;
using Iam.DomainService.Entities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Blocks.Genesis;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Writes <c>OidcLoginRequest</c>-driven audit log events. Failures are logged but never thrown,
    /// preserving the original behavior of the inline helper in <c>AuthorizationFlowService</c>.
    /// </summary>
    public sealed class OidcLoginAuditWriter
    {
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ILogger<OidcLoginAuditWriter> _logger;

        public OidcLoginAuditWriter(
            IAuditLogRepository auditLogRepo,
            IAuthenticationDomainService authenticationDomainService,
            ILogger<OidcLoginAuditWriter> logger)
        {
            _auditLogRepo = auditLogRepo;
            _authenticationDomainService = authenticationDomainService;
            _logger = logger;
        }

        public async Task WriteAsync(OidcLoginRequest request, User user, HttpRequest httpRequest, string eventType, string? details)
        {
            try
            {
                var isFailure = eventType.Contains("failure", StringComparison.OrdinalIgnoreCase)
                    || eventType.Contains("locked", StringComparison.OrdinalIgnoreCase);
                var isSuccess = eventType.Contains("success", StringComparison.OrdinalIgnoreCase);

                await _auditLogRepo.CreateAsync(new AuditLogModel
                {
                    EventType = eventType,
                    UserId = user.ItemId,
                    ClientId = request.ClientId,
                    TenantId = request.TenantId ?? BlocksContext.GetContext()?.TenantId,
                    IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(httpRequest),
                    UserAgent = httpRequest.Headers.UserAgent.ToString(),
                    Severity = isFailure ? IdpConstants.SeverityWarn : IdpConstants.SeverityInfo,
                    Status = isSuccess ? IdpConstants.StatusSuccess : IdpConstants.StatusFailure,
                    Details = details ?? eventType
                });

                var timelineEvent = new UserAuthenticationTimelineEvent
                {
                    UserId = user.ItemId,
                    Event = eventType,
                    ActionBy = "OidcLoginAuditWriter",
                    TenantId = request.TenantId ?? BlocksContext.GetContext()?.TenantId,
                    ClientId = request.ClientId,
                    Outcome = isSuccess ? IdpConstants.StatusSuccess : IdpConstants.StatusFailure,
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(httpRequest?.Headers?.UserAgent.ToString() ?? string.Empty),
                    IpAddresses = OidcRedirectUrlBuilder.GetClientIpAddress(httpRequest),
                    RiskLevel = isFailure ? "medium" : "low",
                    ReasonCode = isFailure ? eventType : null
                };

                await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, timelineEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write OIDC login audit event {EventType} for user {UserId}", eventType, user.ItemId);
            }
        }
    }
}
