namespace Iam.DomainService.Accounts
{
    public class RecoveryUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? CaptchaCode { get; set; }

        // OIDC context of the application the reset was requested from, carried into the
        // recovery email so the user returns there after setting a new password.
        public string? ClientId { get; set; }
        public string? RedirectUri { get; set; }
    }


}
