namespace Iam.DomainService.Accounts
{
    public interface IAdminPasswordResetService
    {
        /// <summary>Sends the user the password-reset mail on behalf of an administrator.</summary>
        Task<BaseAccountResponse> SendResetMailAsync(AdminPasswordResetRequest request);
    }
}
