namespace Authentication.DomainService.OAuth
{
    public static class GrantTypes
    {
        public const string RefreshToken = "refresh_token";
        public const string Password = "password";
        public const string MfaCode = "mfa_code";
        public const string Social = "social";
        public const string AuthCode = "authorization_code";
        public const string BiometricAuthorization = "biometric_authorization";
        public const string ClientCredential = "client_credentials";
        public const string ClientUserCode = "client_user_code";
        public const string SwitchOrganization = "switch_organization";
        public const string SsoConsentCode = "sso_consent";
        public const string ImpersonationCloud = "impersonation_cloud";
        public const string DeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

        /// <summary>
        /// RFC 8693. Used by background workers to redeem a delegation grant for a short-lived
        /// access token carrying the originating user's context.
        /// </summary>
        public const string TokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";
    }
}
