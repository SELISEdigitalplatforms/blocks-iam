namespace Iam.DomainService.Accounts
{
    public class ActivateUserRequest : BaseAccountRequest
    {
        public string? MailPurpose { get; set; }
        public bool PreventPostEvent { get; set; }

        // Invited users are created with no name; they supply it on the activation form.
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

}
