using MongoDB.Bson.Serialization.Attributes;

namespace Blocks.CaptchaDriver;

/// <summary>
/// A captcha configuration as blocks-os stores it: one entry in the tenant's
/// <c>keyValueStores</c> collection, all of them under the single key <c>captcha</c>.
/// </summary>
/// <remarks>
/// Structurally mirrors blocks-os's <c>CaptchaConfigResult</c>, which is the exact shape persisted
/// there. It is redeclared here rather than shared, because the two services are separate repos
/// and this is a consumed wire contract, not shared code.
/// <para>
/// Never carries a secret value — only <see cref="SecretId"/>, a pointer into the secret store.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class CaptchaConfigRecord
{
    /// <summary>
    /// Record id, stamped from the store entry's <c>ItemId</c> on read.
    /// </summary>
    /// <remarks>
    /// blocks-os deliberately does not persist this inside the stored value — the store entry is
    /// the identity, and keeping a copy in the payload would leave two things to hold in step.
    /// So it is always absent in the document and always filled in by the repository.
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    public bool IsEnable { get; set; }

    public string Provider { get; set; } = string.Empty;

    /// <summary>Site key (public).</summary>
    public string CaptchaKey { get; set; } = string.Empty;

    public string CaptchaGenerator { get; set; } = string.Empty;

    /// <summary>Secret store item id, or null when no secret has been set.</summary>
    public string? SecretId { get; set; }

    /// <summary>Projects onto the driver's own configuration model.</summary>
    public CaptchaConfiguration ToConfiguration() => new()
    {
        CaptchaKey = CaptchaKey,
        CaptchaSecret = string.Empty,
        SecretId = SecretId,
        Provider = Provider,
        CaptchaGenerator = CaptchaGenerator,
        IsEnable = IsEnable
    };
}
