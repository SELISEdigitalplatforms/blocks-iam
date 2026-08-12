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
        public string? OrganizationName { get; set; }
        public string? OrganizationDescription { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(); // For any additional info from client that doesn't fit into existing properties
    }
}