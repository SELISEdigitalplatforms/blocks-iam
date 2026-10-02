using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.SignupLinks;

[BsonIgnoreExtraElements]
public class SignupLink : BaseEntity
{
    public string CodeHash { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ConfigurationId { get; set; } = string.Empty;
    // OrganizationId inherited from BaseEntity — always concrete at generation
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? ForwardedTo { get; set; }
    public SignupLinkCredentialMode CredentialMode { get; set; }

    /// <summary>Frozen from the configuration at generation.</summary>
    public SignupLinkMode Mode { get; set; }

    /// <summary>
    /// Frozen at generation too, so editing the configuration cannot change what an
    /// invitation already in someone's inbox does.
    /// </summary>
    public bool SignInAfterActivation { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    // Language inherited from BaseEntity
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>
    /// How many redemptions this link allows. <see cref="UnlimitedMaxRedemptions"/> means no
    /// cap, in which case only <see cref="ExpiresAtUtc"/> ends it.
    /// </summary>
    public int MaxRedemptions { get; set; } = 1;

    /// <summary>
    /// "No cap." Zero rather than null because null already means "not specified, inherit",
    /// and every configuration written before this feature has null -- were null to mean
    /// unlimited, all of them would silently become unlimited.
    /// </summary>
    public const int UnlimitedMaxRedemptions = 0;

    /// <summary>Default when neither the generate payload nor the configuration says otherwise.</summary>
    public const int DefaultMaxRedemptions = 1;

    /// <summary>
    /// Whether this link may be redeemed again. The one place the unlimited sentinel is
    /// interpreted: a bare <c>RedemptionCount &gt;= MaxRedemptions</c> reads zero as
    /// "exhausted on sight", which is the opposite of what it means.
    /// </summary>
    public static bool HasRedemptionBudget(int redemptionCount, int maxRedemptions) =>
        maxRedemptions == UnlimitedMaxRedemptions || redemptionCount < maxRedemptions;

    /// <summary>
    /// Payload wins, then the configuration's default, then single use. Absent at both levels
    /// has to stay single use: every caller and configuration predating this feature is in
    /// exactly that state, and reading it as unlimited would make all of their links reusable
    /// without anyone asking.
    /// </summary>
    public static int ResolveMaxRedemptions(int? fromRequest, int? fromConfiguration) =>
        fromRequest ?? fromConfiguration ?? DefaultMaxRedemptions;
    public int RedemptionCount { get; set; }
    public SignupLinkStatus Status { get; set; } = SignupLinkStatus.Active;
    public string? CreatedUserId { get; set; }
    public string? CreatedByClientId { get; set; }
    public string? CreatedByOrganizationId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedBy { get; set; }
}
