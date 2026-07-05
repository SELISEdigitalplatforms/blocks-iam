namespace Blocks.CaptchaDriver;

/// <summary>
/// Maps a <see cref="Secret"/> document into a <see cref="CaptchaConfiguration"/>.
/// </summary>
public static class CaptchaConfigurationMapping
{
    /// <summary>
    /// Maps a <see cref="Secret"/> document into a <see cref="CaptchaConfiguration"/>.
    /// </summary>
    /// <param name="secret">Source secret document. May be <c>null</c>.</param>
    /// <returns>Mapped configuration, or <c>null</c> when the source has no key/value pairs.</returns>
    public static CaptchaConfiguration? MapToCaptchaConfiguration(Secret? secret)
    {
        if (secret?.KeyValuePairs is not { } values)
        {
            return null;
        }

        var isEnable = false;
        if (values.TryGetValue(CaptchaSecretKeys.IsEnable, out var rawIsEnable)
            && bool.TryParse(rawIsEnable, out var parsed))
        {
            isEnable = parsed;
        }

        return new CaptchaConfiguration
        {
            CaptchaKey = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaKey) ?? string.Empty,
            CaptchaSecret = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaSecret) ?? string.Empty,
            Provider = values.GetValueOrDefault(CaptchaSecretKeys.Provider) ?? string.Empty,
            CaptchaGenerator = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaGenerator) ?? string.Empty,
            IsEnable = isEnable
        };
    }
}
