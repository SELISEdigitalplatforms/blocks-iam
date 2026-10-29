using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Services;
using Iam.DomainService.Users.ResponseModel;

namespace Iam.DomainService.Accounts
{
    public interface ILoginPolicySettingsService
    {
        /// <summary>The tenant's sign-in policy; everything on when it never saved one.</summary>
        Task<GetLoginPolicyResponse> GetAsync();

        /// <summary>Saves the tenant's sign-in policy. Refuses a policy that would let nobody sign in.</summary>
        Task<BaseResponse> SaveAsync(TenantLoginPolicy request);
    }

    public sealed class LoginPolicySettingsService : ILoginPolicySettingsService
    {
        public const string LocksEveryoneError = "login_policy_locks_everyone";

        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IAccessGrantService? _accessGrant;

        public LoginPolicySettingsService(IIdentityAccessManagementRepository repository, IAccessGrantService? accessGrant = null)
        {
            _repository = repository;
            _accessGrant = accessGrant;
        }

        public async Task<GetLoginPolicyResponse> GetAsync()
        {
            var configuration = await _repository.GetTenantConfigurationAsync();
            return new GetLoginPolicyResponse
            {
                IsSuccess = true,
                LoginPolicy = configuration?.LoginPolicy ?? new TenantLoginPolicy()
            };
        }

        public async Task<BaseResponse> SaveAsync(TenantLoginPolicy request)
        {
            // Tenant-wide, like the signup settings next to it, so it belongs to the system user.
            if (!_accessGrant.AuthorizeSystemOnlyIfWired("login-policy"))
            {
                return new BaseResponse { IsSuccess = false, Errors = AccessGrantErrors.Create() };
            }

            if (!request.AllowsAnySignIn())
            {
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { LocksEveryoneError, "At least one place (embedded or OIDC) and one method (password or social) must stay on." }
                    }
                };
            }

            var policy = new TenantLoginPolicy
            {
                AllowEmbedded = request.AllowEmbedded,
                AllowOidc = request.AllowOidc,
                AllowPassword = request.AllowPassword,
                AllowSocial = request.AllowSocial
            };

            await _repository.SaveLoginPolicyAsync(policy, BlocksContext.GetContext()?.UserId);
            return new BaseResponse { IsSuccess = true };
        }
    }
}
