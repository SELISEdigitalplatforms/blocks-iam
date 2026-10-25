using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    /// <summary>
    /// Optional token lifetimes of one OIDC client, in minutes. A null field means "use the project
    /// value" from <c>IdentityConfiguration</c>. Stored in the tenant key-value store, one entry per
    /// client, so the Genesis <c>OidcClientRegistration</c> entity stays unchanged.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class OidcClientTokenLifetimes
    {
        public const int MinMinutes = 1;
        public const int MaxAccessTokenMinutes = 1440;
        public const int MaxRefreshTokenMinutes = 525600;

        /// <summary>The client the entry belongs to. Set by the server; ignored on input.</summary>
        public string? ClientId { get; set; }

        public int? AccessTokenValidForNumberMinutes { get; set; }

        /// <summary>Sliding (idle) refresh-token lifetime.</summary>
        public int? RefreshTokenValidForNumberMinutes { get; set; }

        /// <summary>Hard cap of a refresh-token lineage, fixed at first issue.</summary>
        public int? AbsoluteRefreshTokenValidForNumberMinutes { get; set; }

        public bool HasAnyValue() =>
            AccessTokenValidForNumberMinutes.HasValue
            || RefreshTokenValidForNumberMinutes.HasValue
            || AbsoluteRefreshTokenValidForNumberMinutes.HasValue;

        public static bool IsValidAccessMinutes(int value) => value >= MinMinutes && value <= MaxAccessTokenMinutes;

        public static bool IsValidRefreshMinutes(int value) => value >= MinMinutes && value <= MaxRefreshTokenMinutes;
    }
}
