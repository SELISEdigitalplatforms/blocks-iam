namespace Iam.DomainService.Notifications
{
    /// <summary>
    /// Names of the built-in notification configs this service sends. Each value is the config
    /// <c>Name</c> seeded by blocks-logic for <c>SourceService = blocks-iam</c>; used as the
    /// Notifier's <c>ConfigurationName</c>.
    /// </summary>
    public static class BuiltInNotificationNames
    {
        public const string BulkRoleChangeFinished = "iam-bulk-role-change-finished";
        public const string UserRolesChanged = "iam-user-roles-changed";
        public const string UserDeactivated = "iam-user-deactivated";
        public const string PasswordChanged = "iam-password-changed";
        public const string MfaChanged = "iam-mfa-changed";
        public const string ClientCredentialChanged = "iam-client-credential-changed";
        public const string NewLogin = "iam-new-login";
        public const string UserActivated = "iam-user-activated";
        public const string PermissionPropagationFailed = "iam-permission-propagation-failed";
        public const string OrganizationSetupFinished = "iam-organization-setup-finished";
        public const string SessionRevoked = "iam-session-revoked";
        public const string UserUpdated = "iam-user-updated";
        public const string AccountLocked = "iam-account-locked";
        public const string AccountUnlocked = "iam-account-unlocked";
    }
}
