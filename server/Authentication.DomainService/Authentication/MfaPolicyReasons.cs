namespace Authentication.DomainService.Authentication
{
    public static class MfaPolicyReasons
    {
        public const string NoUser = "no_user";
        public const string MfaDisabledGlobally = "mfa_disabled_globally";
        public const string RoleExempt = "role_exempt";
        public const string NoPolicyMatch = "no_policy_match";
        public const string GlobalPolicy = "global_policy";
        public const string RolePolicy = "role_policy";
        public const string ClientPolicy = "client_policy";
        public const string UserEnrolled = "user_enrolled";
    }
}
