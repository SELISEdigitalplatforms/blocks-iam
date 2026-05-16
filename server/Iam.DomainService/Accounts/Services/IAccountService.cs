using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Entities;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;

namespace Iam.DomainService.Accounts
{
    public interface IAccountService
    {
        Task<BaseAccountResponse> ActivateAccountAsync(ActivateUserRequest activateUserRequest);
        Task<BaseAccountResponse> SignupAccountAsync(SignupUserRequest signupUserRequest);
        Task<BaseAccountResponse> RecoverAccountAsync(RecoveryUserRequest recoveryRequest);
        Task<BaseAccountResponse> ResetAccountPasswordAsync(ResetPasswordRequest resetPasswordRequest);
        Task<BaseAccountResponse> ChangePasswordAsync(ChangePasswordRequest changePasswordRequest);
        Task<BaseAccountResponse> ResendActivationAsync(ResendActivationRequest resendActivationRequest);
        Task<ActivationCodeValidationResponse> ValidateAccountActivationCodeAsync(ValidateActivationCodeRequest validateActivationCodeRequest);
        Task<SaveSignUpSettingResponse> SaveSignUpSettingAsync(SaveSignUpSettingRequest request);
        Task<Dictionary<string, object>> GetSignUpSettingAsync();
        Task<BaseAccountResponse> UnlockAccountAsync(string userId); // Admin method to unlock a locked account
        Task SendAccountLockedNotificationAsync(User user, DateTime lockoutUntilUtc); // Send email when account is locked
    }
}
