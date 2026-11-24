namespace Iam.DomainService.Batch
{
    /// <summary>Request-level (400) and item-level (job failures) error codes of the batch upsert.</summary>
    public static class BatchErrorCodes
    {
        // Request level: the whole request is refused and nothing is saved.
        public const string ItemsRequired = "items_required";
        public const string ItemsLimitExceeded = "items_limit_exceeded";
        public const string DuplicateRef = "duplicate_ref";
        public const string DuplicateEmail = "duplicate_email";
        public const string ClientReferenceTooLong = "client_reference_too_long";

        // Item level, API stage.
        public const string RefTooLong = "ref_too_long";
        public const string EmailRequired = "email_required";
        public const string EmailInvalid = "email_invalid";
        public const string EmailTooLong = "email_too_long";
        public const string FirstNameTooLong = "first_name_too_long";
        public const string LastNameTooLong = "last_name_too_long";
        public const string PhoneInvalid = "phone_invalid";
        public const string MembershipsLimitExceeded = "memberships_limit_exceeded";
        public const string OrganizationDuplicate = "organization_duplicate";
        public const string TooManyPermissions = "too_many_permissions";
        public const string MailTemplateTooLong = "mail_template_too_long";
        public const string MailLanguageTooLong = "mail_language_too_long";

        // Item level, worker stage.
        public const string OrganizationRequired = "organization_required";
        public const string OrganizationNotSupported = "organization_not_supported";
        public const string OrganizationNotFound = "organization_not_found";
        public const string OrganizationDisabled = "organization_disabled";
        public const string OrganizationNotAllowed = "organization_not_allowed";
        public const string RoleNotFound = "role_not_found";
        public const string ManageUserRolesRequired = "manage_user_roles_required";
        public const string UpdateUserProfilesRequired = "update_user_profiles_required";
        public const string GrantNotAllowed = "grant_not_allowed";
        public const string EmailConflict = "email_conflict";
        public const string WriteFailed = "write_failed";

        // Job level (BatchJob.Error).
        public const string QueueUnavailable = "queue_unavailable";
        public const string TenantMismatch = "tenant_mismatch";
        public const string ProcessingFailed = "processing_failed";

        // Mail phase, per mail (BatchMailResult.Code).
        public const string UserExists = "user_exists";
        public const string NoNewMembership = "no_new_membership";
        public const string TemplateRequired = "template_required";
        public const string TemplateNotFound = "template_not_found";
        public const string QueueError = "queue_error";
        public const string ImportFailed = "import_failed";

        private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
        {
            [RefTooLong] = $"Ref can have at most {BatchLimits.MaxRefLength} characters",
            [EmailRequired] = "Email is required",
            [EmailInvalid] = "Email is invalid",
            [EmailTooLong] = $"Email can have at most {BatchLimits.MaxEmailLength} characters",
            [FirstNameTooLong] = $"First name can have at most {BatchLimits.MaxNameLength} characters",
            [LastNameTooLong] = $"Last name can have at most {BatchLimits.MaxNameLength} characters",
            [PhoneInvalid] = $"Phone number must start with + and have at most {BatchLimits.MaxPhoneLength} characters",
            [MembershipsLimitExceeded] = $"A user can have at most {BatchLimits.MaxMemberships} memberships",
            [OrganizationDuplicate] = "The organization is listed more than once",
            [TooManyPermissions] = $"A maximum of {Users.UserIdentityRules.MaxPermissionsPerUser} permissions can be held in one organization",
            [MailTemplateTooLong] = $"A mail template name can have at most {BatchLimits.MaxMailTemplateLength} characters",
            [MailLanguageTooLong] = $"A mail language can have at most {BatchLimits.MaxMailLanguageLength} characters",
            [OrganizationRequired] = "Organization is required when multi organization is enabled",
            [OrganizationNotSupported] = "Only the default organization can be used when multi organization is disabled",
            [OrganizationNotFound] = "Organization does not exist",
            [OrganizationDisabled] = "Organization is disabled",
            [OrganizationNotAllowed] = "Other org user can not add/update",
            [RoleNotFound] = "Role does not exist in the organization",
            [ManageUserRolesRequired] = "Changing the roles of a member requires the manage-user-roles permission",
            [UpdateUserProfilesRequired] = "Changing a user's profile requires the update-user-profiles permission",
            [GrantNotAllowed] = "You are not allowed to grant this access",
            [EmailConflict] = "The user could not be saved because another record already has the same key",
            [WriteFailed] = "The user could not be saved",
            [UserExists] = "The user already existed and no existing-user template was given",
            [NoNewMembership] = "The existing user was not added to a new organization",
            [TemplateRequired] = "A mail was requested without a template",
            [TemplateNotFound] = "The template does not exist for the organization or the default organization in this language",
            [QueueError] = "The mail could not be queued",
            [ImportFailed] = "The user was not imported"
        };

        /// <summary>The English message of an item error code.</summary>
        public static string MessageFor(string code) => Messages.TryGetValue(code, out var message) ? message : code;

        public static BatchItemError Error(string field, string code) =>
            new() { Field = field, Code = code, Message = MessageFor(code) };
    }

    /// <summary>The limits of one batch request.</summary>
    public static class BatchLimits
    {
        public const int MaxItems = 100;
        public const int MaxMemberships = 20;
        public const int MaxRefLength = 128;
        public const int MaxEmailLength = 254;
        public const int MaxClientReferenceLength = 200;
        public const int MaxNameLength = 150;
        public const int MaxPhoneLength = 20;
        public const int MaxMailTemplateLength = 200;
        public const int MaxMailLanguageLength = 20;

        /// <summary>The mail language when an item does not set one.</summary>
        public const string DefaultMailLanguage = "en-US";
    }
}
