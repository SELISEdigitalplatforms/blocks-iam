namespace Authentication.DomainService.Shared
{
    public static class AuthenticationConstants
    {
        #region Organizations

        public const string DefaultOrganizationId = "default";

        #endregion

        #region Severity

        public const string SeverityInfo = "INFO";
        public const string SeverityWarn = "WARN";
        public const string SeverityError = "ERROR";
        public const string SeverityCritical = "CRITICAL";

        #endregion

        #region Status

        public const string StatusSuccess = "success";
        public const string StatusFailure = "failure";
        public const string StatusSent = "sent";
        public const string StatusDelivered = "delivered";

        #endregion

        #region PKCE

        public const string PkceMethodS256 = "S256";

        #endregion

        #region Scopes

        public const string OpenIdProfileEmailScope = "openid profile email";

        #endregion

        #region Session timeouts

        public const int MaxIdpSessionHours = 168;
        public const int DefaultIdpSessionIdleHours = 24;
        public const int DefaultIdpSessionAbsoluteHours = 5;

        #endregion

        #region Backchannel retry / timeout

        public const int BackchannelRetryBackoffMilliseconds = 250;
        public const int BackchannelLogoutMaxAttempts = 3;
        public const int BackchannelTimeoutSeconds = 100;

        #endregion

        #region Cache TTL (seconds)

        public const int SocialAuthorizationUrlCacheTtlSeconds = 300;
        public const int OidcAuthorizationCodeCacheTtlSeconds = 600;
        public const int OidcStateCacheTtlSeconds = 300;
        public const int IdpFlowCacheTtlSeconds = 600;

        #endregion

        #region Cookies

        public const int IdpSessionCookieTtlDays = 30;

        #endregion

        #region Token lifetime

        public const int MinAccessTokenLifetimeSeconds = 60;
        public const int SecondsPerMinute = 60;
        public const int MinTokenLifetimeMinutes = 1;

        #endregion

        #region Outbound HTTP

        public const int OutboundRequestLocalhostTimeoutMinutes = 5;

        #endregion

        #region URIs

        public const string AppleAuthUrl = "https://appleid.apple.com";
        public const string GithubUserEmailsUrl = "https://api.github.com/user/emails";
        public const string FallbackIssuer = "https://localhost:5000";
        public const string ProtectedApiAudience = "api://blocks-protected-api";
        public const string LocalhostDefaultUrl = "https://localhost:5000";

        #endregion
    }
}