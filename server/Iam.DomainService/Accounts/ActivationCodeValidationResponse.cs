using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class ActivationCodeValidationResponse : BaseResponse
    {
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Names already held for the account, so the activation form can prefill rather
        /// than ask a self-service signup for details it just supplied. Empty for invited
        /// users, who genuinely have not given them yet.
        /// </summary>
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}
