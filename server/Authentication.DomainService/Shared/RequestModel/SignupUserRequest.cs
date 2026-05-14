namespace Authentication.DomainService.Shared.RequestModel
{
	public class SignupUserRequest
	{
		public string Email { get; set; } = string.Empty;
		public string? CaptchaCode { get; set; }
		public string? MailPurpose { get; set; }
		// Flow selector: false = email signup, true = SSO signup
		public bool IsSsoSignup { get; set; }

		// SSO-related optional fields
		public string? Provider { get; set; }
		public string? ExternalUserId { get; set; }

		// Optional profile fields
		public string? FirstName { get; set; }
		public string? LastName { get; set; }
		public string? PhoneNumber { get; set; }

		// Optional org creation during signup
		public bool CreateOrganizationDuringSignup { get; set; }
		public string? OrganizationName { get; set; }
		public string? OrganizationDescription { get; set; }
		public List<string>? OrganizationDefaultRoles { get; set; }
	}
}
