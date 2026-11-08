namespace Iam.DomainService.Actions
{
    /// <summary>
    /// Names of the actions IAM publishes through <c>IActionPublisher</c>. They match the master
    /// action list kept in the Blocks OS configuration.
    /// </summary>
    public static class IamActionNames
    {
        public const string UserCreated = "iam.user.created";
        public const string UserSignedUp = "iam.user.signed_up";
        public const string UserPasswordChanged = "iam.user.password_changed";
        public const string UserLocked = "iam.user.locked";
        public const string UserUnlocked = "iam.user.unlocked";
        public const string UserMfaChanged = "iam.user.mfa_changed";
        public const string UserUpdated = "iam.user.updated";
        public const string UserActivated = "iam.user.activated";
        public const string UserDeactivated = "iam.user.deactivated";
        public const string UserRolesChanged = "iam.user.roles_changed";
        public const string OrganizationCreated = "iam.organization.created";
        public const string OrganizationUpdated = "iam.organization.updated";

        /// <summary>How a self-signup user arrived, sent as <c>method</c> on <see cref="UserSignedUp"/>.</summary>
        public static class SignupMethods
        {
            public const string Email = "email";
            public const string Social = "social";
            public const string SignupLink = "signup_link";
        }
    }
}
