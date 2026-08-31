namespace Iam.DomainService.Accounts
{
    public class SignupUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? CaptchaCode { get; set; }
        public string? MailPurpose { get; set; }

        // Flow selector: false = email signup, true = SSO signup
        public bool IsSsoSignup { get; set; } // Need clarification on whether this is necessary, as Provider field can also indicate this

        // SSO-related optional fields
        public string? Provider { get; set; }
        public string? ExternalUserId { get; set; }

        // Optional profile fields
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }

        // OIDC context of the application the user signed up from. Carried into the
        // activation email so the "log in" step returns them to that application
        // instead of the IAM root login. Absent for portal-initiated invites.
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }

        // Optional org creation during signup
        public bool CreateOrganizationDuringSignup { get; set; }

        // Kept for backward compatibility: the IAM signup form posts these two flat fields.
        // Organization.Name wins when both are supplied.
        public string? OrganizationName { get; set; }
        public string? OrganizationDescription { get; set; }

        /// <summary>
        /// Full organization profile, for callers that collect it (a Construct multi-step
        /// signup, for example). Only <see cref="SignupOrganizationInfo.Name"/> is required, and
        /// only when <see cref="CreateOrganizationDuringSignup"/> is set.
        /// </summary>
        public SignupOrganizationInfo? Organization { get; set; }

        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(); // For any additional info from client that doesn't fit into existing properties

        /// <summary>
        /// The organization name to create, from either the nested object or the legacy flat
        /// field. Trimmed; null when neither was supplied.
        /// </summary>
        public string? ResolveOrganizationName()
        {
            var name = !string.IsNullOrWhiteSpace(Organization?.Name)
                ? Organization!.Name
                : OrganizationName;

            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        /// <summary>
        /// The organization description, from either the nested object or the legacy flat field.
        /// </summary>
        public string? ResolveOrganizationDescription()
        {
            return !string.IsNullOrWhiteSpace(Organization?.Description)
                ? Organization!.Description
                : OrganizationDescription;
        }
    }
}