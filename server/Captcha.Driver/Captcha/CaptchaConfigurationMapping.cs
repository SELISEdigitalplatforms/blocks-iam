namespace Blocks.CaptchaDriver
{
    public static class CaptchaConfigurationMapping
    {
        public static CaptchaConfiguration MapToCaptchaConfiguration(Secret secret)
        {
            if (secret?.KeyValuePairs is not { } values)
            {
                return null;
            }

            return new CaptchaConfiguration
            {
                CaptchaKey = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaKey),
                CaptchaSecret = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaSecret),
                Provider = values.GetValueOrDefault(CaptchaSecretKeys.Provider),
                CaptchaGenerator = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaGenerator),
                IsEnable = bool.TryParse(values.GetValueOrDefault(CaptchaSecretKeys.IsEnable), out var isEnable) && isEnable
            };
        }
    }
}