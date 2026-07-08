namespace Iam.DomainService.Utilities
{
    public static class IdpConstants
    {
        public const string AuthenticationQueue = "blocks_idp_authentication_listener";
        public const string IamQueue = "blocks_iam_listener_idp";
        public const string IamOrgQueue = "blocks_idp_iam_org_listener";
        public const string MailQueue = "blocks_email_listener";
        public const string MfaQueueName = "blocks_idp_mfa_listener";
    }
}