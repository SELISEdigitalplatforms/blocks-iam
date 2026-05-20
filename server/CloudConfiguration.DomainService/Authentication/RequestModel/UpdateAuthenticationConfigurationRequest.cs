namespace CloudConfiguration.DomainService.Authentication
{
    public class UpdateAuthenticationConfigurationRequest
    {
        public string ItemId { get; set; }
        public int RefreshTokenValidForNumberMinutes { get; set; }
        public int AbsoluteRefreshTokenValidForNumberMinutes { get; set; }
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; }
        public int AccountLockDurationInMinutes { get; set; }
        public int AccessTokenValidForNumberMinutes { get; set; }
        public int RememberMeRefreshTokenValidForNumberMinutes { get; set; }
        public List<string> AllowedGrantTypes { get; set; }
    }
}
