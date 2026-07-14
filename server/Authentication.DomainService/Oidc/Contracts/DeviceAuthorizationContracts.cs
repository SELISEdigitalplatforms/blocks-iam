using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Idp.DomainService.Oidc.Contracts
{
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public sealed class DeviceAuthorizationRequestModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("n");
        public string DeviceCodeHash { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string RequestedScopes { get; set; } = string.Empty;
        public string Status { get; set; } = DeviceAuthorizationStatus.Pending;
        public string? UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? DeniedAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
        public DateTime LastPollAt { get; set; } = DateTime.UtcNow;
        public int PollIntervalSeconds { get; set; } = 5;
        public int PollsObserved { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceInfo { get; set; }
    }

    public static class DeviceAuthorizationStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Denied = "Denied";
        public const string Consumed = "Consumed";
        public const string Expired = "Expired";
    }

    public sealed class DeviceAuthorizationRequest
    {
        [Required]
        public string ClientId { get; set; } = string.Empty;
        public string? Scope { get; set; }
    }

    public sealed class DeviceAuthorizationResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = string.Empty;

        [JsonPropertyName("verification_uri_complete")]
        public string? VerificationUriComplete { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }
    }

    public sealed class DeviceInteractionRequest
    {
        [Required]
        public string UserCode { get; set; } = string.Empty;
        public string? TenantId { get; set; }
    }

    public sealed class DeviceInteractionResponse
    {
        [JsonPropertyName("redirect")]
        public string Redirect { get; set; } = string.Empty;

        [JsonPropertyName("interactionId")]
        public string InteractionId { get; set; } = string.Empty;
    }

    public sealed class DeviceConsentPayload
    {
        [JsonPropertyName("clientName")]
        public string ClientName { get; set; } = string.Empty;

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; set; } = new();

        [JsonPropertyName("tenant")]
        public string Tenant { get; set; } = string.Empty;

        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("userCode")]
        public string UserCode { get; set; } = string.Empty;
    }

    public sealed class DeviceApproveRequest
    {
        [Required]
        public string InteractionId { get; set; } = string.Empty;

        [Required]
        public string Decision { get; set; } = "allow";
    }

    public sealed class DeviceApproveResponse
    {
        [JsonPropertyName("redirect")]
        public string Redirect { get; set; } = "/device/success";

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    public sealed class DeviceInteractionContext
    {
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; } = string.Empty;

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;

        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
}

namespace Authentication.DomainService.Oidc.Contracts
{
    using Idp.DomainService.Oidc.Contracts;
    using Microsoft.AspNetCore.Mvc;

    public static class DeviceEndpointErrors
    {
        public static IActionResult UnauthorizedClient(string description) =>
            new BadRequestObjectResult(new { error = "unauthorized_client", error_description = description });

        public static IActionResult InvalidRequest(string description) =>
            new BadRequestObjectResult(new { error = "invalid_request", error_description = description });

        public static IActionResult InvalidScope(string description) =>
            new BadRequestObjectResult(new { error = "invalid_scope", error_description = description });

        public static IActionResult InvalidGrant(string description) =>
            new BadRequestObjectResult(new { error = "invalid_grant", error_description = description });

        public static IActionResult AccessDenied(string description) =>
            new BadRequestObjectResult(new { error = "access_denied", error_description = description });

        public static IActionResult ExpiredToken(string description) =>
            new BadRequestObjectResult(new { error = "expired_token", error_description = description });

        public static IActionResult AuthorizationPending(string description) =>
            new BadRequestObjectResult(new { error = "authorization_pending", error_description = description });

        public static IActionResult SlowDown(string description, int interval) =>
            new BadRequestObjectResult(new { error = "slow_down", error_description = description, interval });
    }
}