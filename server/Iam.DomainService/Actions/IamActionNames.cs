namespace Iam.DomainService.Actions
{
    /// <summary>
    /// Names of the actions IAM publishes through <c>IActionPublisher</c>. They match the master
    /// action list kept in the Blocks OS configuration.
    /// </summary>
    public static class IamActionNames
    {
        public const string UserCreated = "iam.user.created";
        public const string UserUpdated = "iam.user.updated";
        public const string UserActivated = "iam.user.activated";
        public const string UserDeactivated = "iam.user.deactivated";
        public const string UserRolesChanged = "iam.user.roles_changed";
        public const string OrganizationCreated = "iam.organization.created";
        public const string OrganizationUpdated = "iam.organization.updated";
    }
}
