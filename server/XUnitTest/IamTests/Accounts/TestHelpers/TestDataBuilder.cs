using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;

namespace XUnitTest.IamTests.Accounts.TestHelpers
{
    /// <summary>
    /// Fluent builders that produce deterministic instances of IAM DomainService
    /// domain entities and DTOs for use in unit tests.
    /// </summary>
    internal static class TestDataBuilder
    {
        public const string DefaultTenantId = "tenant-1";
        public const string DefaultUserId = "user-1";
        public const string DefaultOrganizationId = "org-1";
        public const string DefaultSalt = "salt";

        public static User CreateUser(string? itemId = null, string? email = null, bool active = true, bool isVerified = true)
        {
            return new User
            {
                ItemId = itemId ?? DefaultUserId,
                Email = email ?? "user@example.com",
                UserName = email ?? "user@example.com",
                FirstName = "Jane",
                LastName = "Doe",
                Active = active,
                IsVerified = isVerified,
                MailPurpose = "AccountActivation",
                CreatedBy = "system",
                Language = "en-US",
                Password = "hashed:old"
            };
        }

        public static UserActivityEvent CreateUserActivityEvent(
            string? userId = null,
            string? @event = null,
            UserActivityCategory category = UserActivityCategory.Account,
            string? source = "iam-account")
        {
            return new UserActivityEvent
            {
                UserId = userId ?? DefaultUserId,
                Category = category,
                Event = @event ?? "Activate_Account",
                Source = source
            };
        }

        public static IamConfiguration CreateIamConfiguration(
            bool isOidcEnabled = false,
            int activationLifetimeMinutes = 60 * 24,
            int recoverLifetimeMinutes = 10,
            bool logoutOnPasswordChange = true,
            string accountActionBaseUrl = "https://app.example.com")
        {
            return new IamConfiguration
            {
                IsOidcEnabled = isOidcEnabled,
                AccountActivationPath = "Account/Activate",
                RecoverAccountPath = "Account/Recover",
                AccountActionBaseUrl = accountActionBaseUrl,
                UseAccountActionBaseUrlAsDefault = true,
                ActivationUrlLifetimeInMinutes = activationLifetimeMinutes,
                RecoverAccountUrlLifetimeInMinutes = recoverLifetimeMinutes,
                LogoutOnPasswordChange = logoutOnPasswordChange,
                PasswordStrengthCheckerRegex = string.Empty
            };
        }

        public static TenantConfiguration CreateTenantConfiguration(
            bool isEmailPasswordSignUpEnabled = true,
            bool isSsoSignUpEnabled = false,
            List<string>? defaultRoles = null,
            List<string>? defaultPermissions = null)
        {
            return new TenantConfiguration
            {
                ItemId = "tenantConfig-1",
                IsEmailPasswordSignUpEnabled = isEmailPasswordSignUpEnabled,
                IsSSoSignUpEnabled = isSsoSignUpEnabled,
                DefaultRolesForNewUserOnSignUp = defaultRoles ?? new List<string> { "user" },
                DefaultPermissionsForNewUserOnSignUp = defaultPermissions ?? new List<string> { "perm.read" },
                AllowOrgCreationFromSignup = false
            };
        }

        public static List<UserKeyMap> CreateUserKeyMaps(string userId, int count)
        {
            var list = new List<UserKeyMap>();
            for (var i = 0; i < count; i++)
            {
                list.Add(new UserKeyMap
                {
                    ItemId = Guid.NewGuid().ToString(),
                    Key = $"key-{i}-{Guid.NewGuid():N}",
                    UserId = userId,
                    IssueDate = DateTime.UtcNow,
                    ExpireDate = DateTime.UtcNow.AddMinutes(15),
                    Value = "https://app.example.com/recover",
                    MailPurpose = "AccountActivation",
                    Activated = false
                });
            }
            return list;
        }

        /// <summary>
        /// Builds a configured <see cref="BlocksContext"/> for the test. The context is also
        /// installed via <see cref="BlocksContext.SetContext"/> and <c>IsTestMode</c> is enabled.
        /// </summary>
        public static BlocksContext InstallBlocksContext(
            string? tenantId = null,
            string? userId = null,
            string? organizationId = null)
        {
            BlocksContext.IsTestMode = true;
            var ctx = BlocksContext.Create(
                tenantId: tenantId ?? DefaultTenantId,
                roles: null,
                userId: userId ?? DefaultUserId,
                impersonated: false,
                isAuthenticated: true,
                requestUri: "https://test/iam",
                organizationId: organizationId ?? DefaultOrganizationId,
                permissions: null,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: "user@example.com",
                userName: "tester",
                phoneNumber: null,
                displayName: "Tester",
                oauthToken: null,
                originalTenantId: tenantId ?? DefaultTenantId,
                impersonationSessionId: null,
                applicationDomain: "test");
            BlocksContext.SetContext(ctx);
            return ctx;
        }

        public static void ResetBlocksContext()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }
    }
}
