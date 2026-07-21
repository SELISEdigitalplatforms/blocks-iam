using System.Text.Json.Serialization;

namespace Authentication.DomainService.Authentication
{
    public sealed class LogoutRequest
    {
        public string? RefreshToken { get; set; }
    }

    public sealed class LogoutAllRequest
    {
        public bool UseBackchannel { get; set; } = false;
    }

    public sealed class LogoutResponse
    {
        public bool IsSuccess { get; set; }

        [JsonIgnore]
        public string? IdpSessionId { get; set; }

        [JsonIgnore]
        public IReadOnlyList<string> IdpSessionIds { get; set; } = Array.Empty<string>();
    }
}
