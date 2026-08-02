using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Orchestrator for the RFC 8628 section 3.1 device authorization endpoint. Wraps
    /// <see cref="IDeviceAuthorizationService"/> with HTTP-friendly error mapping.
    /// </summary>
    public sealed class DeviceAuthorizationEndpoint
    {
        private readonly IDeviceAuthorizationService _service;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<DeviceAuthorizationEndpoint> _logger;

        public DeviceAuthorizationEndpoint(
            IDeviceAuthorizationService service,
            IHttpContextAccessor httpContextAccessor,
            ILogger<DeviceAuthorizationEndpoint> logger)
        {
            _service = service;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<IActionResult> HandleAsync(HttpRequest request, CancellationToken ct = default)
        {
            SetNoStoreHeaders(request.HttpContext);

            if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "POST required" });
            }

            if (!request.HasFormContentType)
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "application/x-www-form-urlencoded required" });
            }

            var form = await request.ReadFormAsync(ct);
            var dto = new DeviceAuthorizationRequest
            {
                ClientId = form["client_id"].ToString(),
                Scope = form["scope"].ToString()
            };

            try
            {
                var response = await _service.RequestAsync(dto, request, ct);
                SetNoStoreHeaders(_httpContextAccessor.HttpContext);
                return new OkObjectResult(response);
            }
            catch (DeviceAuthorizationException ex)
            {
                _logger.LogWarning("Device authorization rejected: {Error} {Description}", ex.Error, ex.ErrorDescription);
                return new BadRequestObjectResult(new { error = ex.Error, error_description = ex.ErrorDescription });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Device authorization failed unexpectedly");
                return new ObjectResult(new { error = "server_error" }) { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }

        private static void SetNoStoreHeaders(HttpContext? http)
        {
            if (http == null)
            {
                return;
            }

            http.Response.Headers["Cache-Control"] = "no-store";
            http.Response.Headers["Pragma"] = "no-cache";
        }
    }
}
