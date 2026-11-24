namespace Iam.DomainService.Users
{
    /// <summary>
    /// One permission per user-management action. They replace the single
    /// <c>blocks-iam::iam::mutate-users</c>, so a tenant can let a role invite people without
    /// letting it edit profiles, change roles, remove members or lock accounts.
    /// </summary>
    public static class UserPermissions
    {
        /// <summary>users/create: invite a new account, or add an existing account that is not yet a member.</summary>
        public const string CreateUsers = "blocks-iam::iam::create-users";

        /// <summary>users/{id}: edit another user's profile (name, phone, language, image, tags, attributes).</summary>
        public const string UpdateUserProfiles = "blocks-iam::iam::update-user-profiles";

        /// <summary>users/access and users/roles/bulk (+ preview): set a user's roles and permissions in the organization.</summary>
        public const string ManageUserRoles = "blocks-iam::iam::manage-user-roles";

        /// <summary>users/revoke-access: remove a user from the organization.</summary>
        public const string RevokeUserAccess = "blocks-iam::iam::revoke-user-access";

        /// <summary>users/deactivate: switch the whole account off. Default organization callers only.</summary>
        public const string DeactivateUsers = "blocks-iam::iam::deactivate-users";

        /// <summary>users/activate: switch the whole account back on. Default organization callers only.</summary>
        public const string ActivateUsers = "blocks-iam::iam::activate-users";

        /// <summary>iam/batch/users and iam/batch/jobs/{jobId}: submit an asynchronous batch user upsert and read its job.</summary>
        public const string ImportUsers = "blocks-iam::iam::import-users";

        /// <summary>Every user-management permission above.</summary>
        public static readonly IReadOnlyList<string> All =
        [
            CreateUsers,
            UpdateUserProfiles,
            ManageUserRoles,
            RevokeUserAccess,
            DeactivateUsers,
            ActivateUsers,
            ImportUsers
        ];
    }
}
