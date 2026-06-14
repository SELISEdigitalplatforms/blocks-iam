namespace CloudConfiguration.DomainService.Authentication
{
    public class UpdateAuthenticationConfigurationRequest
    {
        public string ItemId { get; set; }

        public int RefreshTokenValidForNumberMinutes { get; set; }

        public int AbsoluteRefreshTokenValidForNumberMinutes { get; set; }

        public int AccessTokenValidForNumberMinutes { get; set; }

        public int RememberMeRefreshTokenValidForNumberMinutes { get; set; }

        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; }

        public int AccountLockDurationInMinutes { get; set; }

        public string PublicCertificatePath { get; set; }

        public string AccountActivationPath { get; set; }

        public string AccountVerificationPath { get; set; }

        public string RecoverAccountPath { get; set; }

        public bool? IsOidcEnabled { get; set; }

        public string AccountActionBaseUrl { get; set; }

        public bool? UseAccountActionBaseUrlAsDefault { get; set; }

        public int ActivationUrlLifetimeInMinutes { get; set; }

        public int RecoverAccountUrlLifetimeInMinutes { get; set; }

        public bool? LogoutOnPasswordChange { get; set; }

        public string PasswordStrengthCheckerRegex { get; set; }
    }
}
