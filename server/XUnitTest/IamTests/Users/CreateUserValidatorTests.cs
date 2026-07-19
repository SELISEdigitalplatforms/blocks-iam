using Blocks.Genesis;
using FluentAssertions;
using FluentValidation.TestHelper;
using Iam.DomainService.Configurations;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Moq;

namespace XUnitTest.IamTests.Users
{
    public class CreateUserValidatorTests : IDisposable
    {
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IIamConfigurationRepository> _configRepo = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();

        public CreateUserValidatorTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "user-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            // Defaults: unique username/email, no blacklist, no password regex, single-org tenant.
            _userRepo.Setup(r => r.GetUserByUserNameOrgIdAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User?)null!);
            _userRepo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = string.Empty });
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private CreateUserValidator Create() =>
            new(_userRepo.Object, _configRepo.Object, _resourceRepo.Object);

        private static CreateUserRequest ValidRequest() => new()
        {
            Email = "valid.user@example.com"
        };

        [Fact]
        public async Task Valid_MinimalRequest_Passes()
        {
            var result = await Create().TestValidateAsync(ValidRequest());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Email_Empty_Fails()
        {
            var req = ValidRequest();
            req.Email = "";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task Email_InvalidFormat_Fails()
        {
            var req = ValidRequest();
            req.Email = "not-an-email";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task Email_AlreadyInUse_Fails()
        {
            _userRepo.Setup(r => r.GetUserByUserNameOrgIdAsync("valid.user@example.com", It.IsAny<string>()))
                .ReturnsAsync(new User { ItemId = "existing" });
            var result = await Create().TestValidateAsync(ValidRequest());
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task FirstName_TooLong_Fails()
        {
            var req = ValidRequest();
            req.FirstName = new string('a', 151);
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.FirstName);
        }

        [Fact]
        public async Task LastName_TooLong_Fails()
        {
            var req = ValidRequest();
            req.LastName = new string('b', 151);
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.LastName);
        }

        [Fact]
        public async Task UserName_TooShort_Fails()
        {
            var req = ValidRequest();
            req.UserName = "abc";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.UserName);
        }

        [Fact]
        public async Task UserName_AlreadyExists_Fails()
        {
            var req = ValidRequest();
            req.UserName = "existinguser";
            _userRepo.Setup(r => r.GetUserByUserNameOrgIdAsync("existinguser", It.IsAny<string>()))
                .ReturnsAsync(new User { ItemId = "u" });
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.UserName);
        }

        [Fact]
        public async Task PhoneNumber_WithoutPlus_Fails()
        {
            var req = ValidRequest();
            req.PhoneNumber = "8801700000000";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
        }

        [Fact]
        public async Task PhoneNumber_WithPlus_Passes()
        {
            var req = ValidRequest();
            req.PhoneNumber = "+8801700000000";
            var result = await Create().TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.PhoneNumber);
        }

        [Fact]
        public async Task MfaEnabled_WithNoneMfaType_Fails()
        {
            var req = ValidRequest();
            req.MfaEnabled = true;
            req.UserMfaType = UserMfaType.None;
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.UserMfaType);
        }

        [Fact]
        public async Task Password_Weak_FailsAgainstTenantRegex()
        {
            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = "^(?=.*[A-Z])(?=.*\\d).{8,}$" });
            var req = ValidRequest();
            req.Password = "weak";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public async Task Password_Blacklisted_Fails()
        {
            _userRepo.Setup(r => r.CheckPasswordBlackListedAsync("Password1!", It.IsAny<string>()))
                .ReturnsAsync(true);
            var req = ValidRequest();
            req.Password = "Password1!";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public async Task MultiOrgEnabled_NonExistingOrg_Fails()
        {
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _resourceRepo.Setup(r => r.GetOrganizationById("org-x")).ReturnsAsync((Organization?)null!);
            var req = ValidRequest();
            req.OrganizationId = "org-x";
            var result = await Create().TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
        }

        [Fact]
        public async Task MultiOrgEnabled_DefaultOrg_Passes()
        {
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            var req = ValidRequest();
            req.OrganizationId = "default";
            var result = await Create().TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.OrganizationId);
        }

        [Fact]
        public async Task MultiOrgEnabled_ExistingOrg_Passes()
        {
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _resourceRepo.Setup(r => r.GetOrganizationById("org-1"))
                .ReturnsAsync(new Organization { ItemId = "org-1", Name = "Org One" });
            var req = ValidRequest();
            req.OrganizationId = "org-1";
            var result = await Create().TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.OrganizationId);
        }
    }
}
