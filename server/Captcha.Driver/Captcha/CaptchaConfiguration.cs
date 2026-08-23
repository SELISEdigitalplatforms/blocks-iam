using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Captcha configuration as the driver sees it, whichever store it came from.
/// </summary>
/// <remarks>
/// Two stores feed this type. The current one is the <c>keyValueStores</c> collection written by
/// blocks-os, where the secret lives in Azure Key Vault and only <see cref="SecretId"/> is stored.
/// The legacy one is a document in the <c>Secrets</c> collection with secret key
/// <see cref="CaptchaSecretKeys.SecretKey"/>, which carries the secret inline in
/// <see cref="CaptchaSecret"/>. Exactly one of the two secret members is populated.
/// </remarks>
[BsonIgnoreExtraElements]
public class CaptchaConfiguration : BaseEntity
{
    /// <summary>Site key (public) presented to the browser.</summary>
    public string CaptchaKey { get; set; } = string.Empty;

    /// <summary>
    /// Server-side secret used to verify tokens. Populated from the legacy <c>Secrets</c>
    /// document only; empty when the configuration came from <c>keyValueStores</c>, where
    /// <see cref="SecretId"/> is used instead.
    /// </summary>
    public string CaptchaSecret { get; set; } = string.Empty;

    /// <summary>
    /// Pointer to the secret store entry holding the secret, resolved through
    /// <see cref="ICaptchaSecretResolver"/>. Null for legacy configurations.
    /// </summary>
    public string? SecretId { get; set; }

    /// <summary>Provider identifier (e.g. <c>recaptcha</c>, <c>hcaptcha</c>, <c>bcaptcha</c>).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Generator identifier (optional).</summary>
    public string CaptchaGenerator { get; set; } = string.Empty;

    /// <summary>Whether this configuration is currently active.</summary>
    public bool IsEnable { get; set; }
}
