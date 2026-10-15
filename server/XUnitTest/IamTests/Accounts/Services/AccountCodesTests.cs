using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Accounts;
using Iam.DomainService.Configurations;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Resources;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using XUnitTest.IamTests.Accounts.TestHelpers;

namespace XUnitTest.IamTests.Accounts.Services
{
    /// <summary>
    /// Reset and activation codes end to end: a real validator and a real AccountService over an
    /// in-memory cache, so the codes the service issues are the ones the tests spend.
    /// </summary>
    public class AccountCodesTests : IDisposable
    {
        private const string ExpiredMessage = "The code has expired. Please request a new one to continue";
        private const string StrongPassword = "Str0ng!Passw0rd";

        private readonly Dictionary<string, string> _store = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IIdentityAccessManagementRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IIamConfigurationRepository> _configRepo = new();
        private readonly Mock<ICaptchaConfigurationService> _captchaConfig = new();
        private readonly Mock<IValidator<RecoveryUserRequest>> _recoverValidator = new();
        private readonly List<SendMail> _sentMails = new();
        private readonly Dictionary<string, User> _users = new();

        public AccountCodesTests()
        {
            TestDataBuilder.InstallBlocksContext();

            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync((string k, string v, long _) => { _store[k] = v; return true; });
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => _store.TryGetValue(k, out var v) ? v : null!);
            _cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => _store.ContainsKey(k));
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => _store.Remove(k));

            _repo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(TestDataBuilder.CreateIamConfiguration());
            _repo.Setup(r => r.GetUserByIdAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => id != null && _users.TryGetValue(id, out var u) ? u : null!);
            _repo.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((string email) => _users.Values.FirstOrDefault(u => u.Email == email)!);
            _repo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
            _repo.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetActiveUserKeyMapAsync(It.IsAny<string>())).ReturnsAsync(new List<UserKeyMap>());
            _repo.Setup(r => r.CheckPasswordBlackListedAsync(It.IsAny<string>())).ReturnsAsync(false);

            _iam.Setup(s => s.HashPassword(It.IsAny<string>(), It.IsAny<string?>())).Returns((string p, string? _) => "h:" + p);
            _iam.Setup(s => s.SendEmailAsync(It.IsAny<SendMail>()))
                .ReturnsAsync((SendMail m) => { _sentMails.Add(m); return true; });
            _iam.Setup(s => s.SendActivationToEmailAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User u, string url, string purpose) =>
                {
                    _sentMails.Add(new SendMail { Purpose = purpose, To = new[] { u.Email } });
                    return true;
                });

            _configRepo.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = string.Empty });
            _captchaConfig.Setup(c => c.GetCaptchaConfigurationAsync()).ReturnsAsync((CaptchaConfiguration?)null);
            _recoverValidator.Setup(v => v.ValidateAsync(It.IsAny<RecoveryUserRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        public void Dispose() => TestDataBuilder.ResetBlocksContext();

        private AccountService CreateService()
        {
            var validator = new BaseAccountValidator(_cache.Object, _configRepo.Object, _repo.Object,
                new Mock<ICaptchaService>().Object, _captchaConfig.Object);
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(new Tenant
            {
                DbConnectionString = "x",
                TenantSalt = TestDataBuilder.DefaultSalt,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
            });

            return new AccountService(
                new Mock<ILogger<AccountService>>().Object,
                _repo.Object,
                _iam.Object,
                _cache.Object,
                tenants.Object,
                validator,
                new Mock<IValidator<ChangePasswordRequest>>().Object,
                _recoverValidator.Object,
                new Mock<IUserManagementMutationService>().Object,
                new Mock<IResourceMutationService>().Object,
                new Mock<ICaptchaService>().Object,
                _captchaConfig.Object,
                new Mock<IDbContextProvider>().Object,
                new Mock<IUserActivityDispatcher>().Object,
                null,
                null,
                new SignupOrganizationValidator(),
                new Mock<IOrganizationNameResolver>().Object,
                null,
                null);
        }

        private User AddUser(string id, bool active, UserLifecycleStatus status, bool isVerified = true, string? email = null)
        {
            var user = TestDataBuilder.CreateUser(id, email ?? $"{id}@example.com", active, isVerified);
            user.Status = status;
            _users[id] = user;
            return user;
        }

        private string LatestCode(string purpose, string userId) => _store[AccountCodes.LatestKey(purpose, userId)];

        private async Task<string> IssueResetAsync(AccountService service, User user)
        {
            (await service.ProcessRecoverAccountAsync(user, "RecoverAccount")).Should().BeTrue();
            return LatestCode(AccountCodes.Reset, user.ItemId);
        }

        private async Task<string> IssueActivationAsync(AccountService service, User user)
        {
            (await service.SendReActivationAsync(user)).Should().BeTrue();
            return LatestCode(AccountCodes.Activation, user.ItemId);
        }

        private static void ShouldBeRefused(BaseAccountResponse result)
        {
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Code").WhoseValue.Should().Be(ExpiredMessage);
        }

        private Task<BaseAccountResponse> ResetAsync(AccountService s, string code) =>
            s.ResetAccountPasswordAsync(new ResetPasswordRequest { Code = code, Password = StrongPassword });

        private Task<BaseAccountResponse> ActivateAsync(AccountService s, string code) =>
            s.ActivateAccountAsync(new ActivateUserRequest { Code = code, Password = StrongPassword, PreventPostEvent = true });

        // ---- other cache keys are not codes ----

        [Fact]
        public async Task MfaId_HoldingAUserId_IsRefused_AtResetAndActivate()
        {
            var service = CreateService();
            AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            _store["mfa-123"] = "u1"; // what a TOTP login stores: mfaId -> userId

            ShouldBeRefused(await ResetAsync(service, "mfa-123"));
            ShouldBeRefused(await ActivateAsync(service, "mfa-123"));
            _users["u1"].Active.Should().BeFalse();
            _users["u1"].Password.Should().Be("hashed:old");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task EmptyOrWhitespaceCode_IsRefused(string code)
        {
            var service = CreateService();
            _store[code] = "u1";
            AddUser("u1", active: true, UserLifecycleStatus.Active);

            (await ResetAsync(service, code)).IsSuccess.Should().BeFalse();
            (await ActivateAsync(service, code)).IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task PointerKeyItself_SubmittedAsCode_IsRefused()
        {
            var service = CreateService();
            var user = AddUser("u1", active: true, UserLifecycleStatus.Active);
            await IssueResetAsync(service, user);

            ShouldBeRefused(await ResetAsync(service, AccountCodes.LatestKey(AccountCodes.Reset, "u1")));
        }

        // ---- latest only, single use ----

        [Fact]
        public async Task OlderResetCode_IsRefused_OnceANewerOneIsIssued_AndTheLatestWorksOnce()
        {
            var service = CreateService();
            var user = AddUser("u1", active: true, UserLifecycleStatus.Active);

            var first = await IssueResetAsync(service, user);
            var second = await IssueResetAsync(service, user);
            first.Should().NotBe(second);

            ShouldBeRefused(await ResetAsync(service, first));
            (await ResetAsync(service, second)).IsSuccess.Should().BeTrue();
            _users["u1"].Password.Should().Be("h:" + StrongPassword);

            // Replay of the spent code.
            ShouldBeRefused(await ResetAsync(service, second));
            // The older one does not come back after the latest is spent.
            ShouldBeRefused(await ResetAsync(service, first));
        }

        [Fact]
        public async Task OlderActivationCode_IsRefused_OnceANewerOneIsIssued()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);

            var first = await IssueActivationAsync(service, user);
            var second = await IssueActivationAsync(service, user);

            ShouldBeRefused(await ActivateAsync(service, first));
            (await ActivateAsync(service, second)).IsSuccess.Should().BeTrue();
            _users["u1"].Active.Should().BeTrue();
            ShouldBeRefused(await ActivateAsync(service, second));
        }

        // ---- purpose ----

        [Fact]
        public async Task ResetCode_IsRefused_AtActivate()
        {
            var service = CreateService();
            var user = AddUser("u1", active: true, UserLifecycleStatus.Active);
            var reset = await IssueResetAsync(service, user);

            ShouldBeRefused(await ActivateAsync(service, reset));
            _users["u1"].Password.Should().Be("hashed:old");
            // Still good for what it was issued for.
            (await ResetAsync(service, reset)).IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ActivationCode_IsRefused_AtReset()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            var activation = await IssueActivationAsync(service, user);

            ShouldBeRefused(await ResetAsync(service, activation));
            _users["u1"].Password.Should().Be("hashed:old");
            (await ActivateAsync(service, activation)).IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task AnotherUsersLatestCode_DoesNotMatchThisUsersPointer()
        {
            var service = CreateService();
            var alice = AddUser("alice", active: true, UserLifecycleStatus.Active);
            var bob = AddUser("bob", active: true, UserLifecycleStatus.Active);
            var aliceCode = await IssueResetAsync(service, alice);
            var bobCode = await IssueResetAsync(service, bob);

            // Repoint bob's code at alice: the value lookup says alice, alice's pointer says otherwise.
            _store[bobCode] = "alice";
            ShouldBeRefused(await ResetAsync(service, bobCode));
            _users["alice"].Password.Should().Be("hashed:old");

            (await ResetAsync(service, aliceCode)).IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task CodeWithoutPointer_IssuedBeforeThisChange_IsRefused()
        {
            var service = CreateService();
            AddUser("u1", active: true, UserLifecycleStatus.Active);
            _store["legacy-code"] = "u1";

            ShouldBeRefused(await ResetAsync(service, "legacy-code"));
        }

        [Fact]
        public async Task ProcessMethods_RecheckTheCode_WhenCalledWithoutTheValidator()
        {
            var service = CreateService();
            AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            _store["mfa-123"] = "u1";

            (await service.ProcessResetPasswordAsync(new ResetPasswordRequest { Code = "mfa-123", Password = StrongPassword })).Should().BeFalse();
            (await service.ProcessActivationAsync(new ActivateUserRequest { Code = "mfa-123", PreventPostEvent = true })).Should().BeFalse();
            _users["u1"].Active.Should().BeFalse();
        }

        [Fact]
        public async Task BaseRequestType_HasNoPurpose_AndIsRefused()
        {
            var validator = new BaseAccountValidator(_cache.Object, _configRepo.Object, _repo.Object,
                new Mock<ICaptchaService>().Object, _captchaConfig.Object);
            var user = AddUser("u1", active: true, UserLifecycleStatus.Active);
            var code = await IssueResetAsync(CreateService(), user);

            (await validator.ValidateAsync(new BaseAccountRequest { Code = code })).IsValid.Should().BeFalse();
        }

        // ---- blocked accounts ----

        [Theory]
        [InlineData(UserLifecycleStatus.Disabled)]
        [InlineData(UserLifecycleStatus.Suspended)]
        public async Task Recover_ForBlockedUser_SendsNothing_AndAnswersLikeAnUnknownEmail(UserLifecycleStatus status)
        {
            var service = CreateService();
            AddUser("u1", active: false, status, email: "blocked@example.com");

            var blocked = await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "blocked@example.com" });
            var unknown = await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "nobody@example.com" });

            blocked.IsSuccess.Should().BeTrue();
            blocked.Should().BeEquivalentTo(unknown);
            _sentMails.Should().BeEmpty();
            _store.Should().BeEmpty();
        }

        [Fact]
        public async Task Recover_ForUserDeactivatedBeforeStatusExisted_SendsNothing()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.Active, email: "old@example.com");
            user.DeactivatedAtUtc = DateTime.UtcNow.AddDays(-1);

            (await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "old@example.com" })).IsSuccess.Should().BeTrue();
            _sentMails.Should().BeEmpty();
        }

        [Fact]
        public async Task Activate_ForDisabledUser_IsRefused_EvenWithALiveLatestCode()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            var code = await IssueActivationAsync(service, user);

            // Disabled by an admin after the mail went out.
            user.Status = UserLifecycleStatus.Disabled;
            user.DeactivatedAtUtc = DateTime.UtcNow;

            ShouldBeRefused(await ActivateAsync(service, code));
            (await service.ProcessActivationAsync(new ActivateUserRequest { Code = code, PreventPostEvent = true })).Should().BeFalse();
            user.Active.Should().BeFalse();
            user.Status.Should().Be(UserLifecycleStatus.Disabled);
        }

        [Fact]
        public async Task Recover_ForPendingUser_StillSendsActivation_AndItActivates()
        {
            var service = CreateService();
            AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false, email: "pending@example.com");

            (await service.RecoverAccountAsync(new RecoveryUserRequest { Email = "pending@example.com" })).IsSuccess.Should().BeTrue();
            _sentMails.Should().ContainSingle(m => m.Purpose == "AccountActivation");

            var code = LatestCode(AccountCodes.Activation, "u1");
            (await ActivateAsync(service, code)).IsSuccess.Should().BeTrue();
            _users["u1"].Active.Should().BeTrue();
            _users["u1"].Status.Should().Be(UserLifecycleStatus.Active);
        }

        [Fact]
        public async Task InvitedUser_StoredAsStatusActiveButInactive_StillActivates()
        {
            // An invite whose VerifiedType is not None is stored Status Active, Active false.
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.Active, isVerified: false);
            var code = await IssueActivationAsync(service, user);

            (await ActivateAsync(service, code)).IsSuccess.Should().BeTrue();
        }

        // ---- validate-activation ----

        private Task<ActivationCodeValidationResponse> ValidateAsync(AccountService s, string code, string userId)
        {
            _repo.Setup(r => r.GetUserIdFromKeyMapByKeyAsync(code)).ReturnsAsync(userId);
            return s.ValidateAccountActivationCodeAsync(new ValidateActivationCodeRequest { ActivationCode = code });
        }

        [Fact]
        public async Task ValidateActivation_LiveLatestCode_ReturnsWhatThePageNeeds()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            var code = await IssueActivationAsync(service, user);

            var result = await ValidateAsync(service, code, "u1");

            result.Status.Should().Be(ActivationCodeStatus.Valid);
            result.IsSuccess.Should().BeTrue();
            result.UserId.Should().Be("u1");
            result.FirstName.Should().Be("Jane");
        }

        [Fact]
        public async Task ValidateActivation_SpentCode_ReturnsNoUserData()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            var code = await IssueActivationAsync(service, user);
            (await ActivateAsync(service, code)).IsSuccess.Should().BeTrue();

            var result = await ValidateAsync(service, code, "u1");

            result.Status.Should().Be(ActivationCodeStatus.AlreadyActivated);
            result.UserId.Should().BeEmpty();
            result.FirstName.Should().BeEmpty();
            result.LastName.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidateActivation_SupersededOrExpiredCode_ReturnsNoUserData()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            var old = await IssueActivationAsync(service, user);
            await IssueActivationAsync(service, user);

            var result = await ValidateAsync(service, old, "u1");

            result.Status.Should().Be(ActivationCodeStatus.Expired);
            result.IsSuccess.Should().BeFalse();
            result.UserId.Should().BeEmpty();
            result.FirstName.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidateActivation_SpentResetCode_ReturnsNoUserData()
        {
            var service = CreateService();
            var user = AddUser("u1", active: true, UserLifecycleStatus.Active);
            var reset = await IssueResetAsync(service, user);
            (await ResetAsync(service, reset)).IsSuccess.Should().BeTrue();

            var result = await ValidateAsync(service, reset, "u1");

            result.IsSuccess.Should().BeFalse();
            result.UserId.Should().BeEmpty();
            result.FirstName.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidateActivation_LiveResetCode_ForInactiveUser_IsNotValid()
        {
            var service = CreateService();
            var user = AddUser("u1", active: true, UserLifecycleStatus.Active);
            var reset = await IssueResetAsync(service, user);
            user.Active = false; // made inactive after the mail went out

            var result = await ValidateAsync(service, reset, "u1");

            result.Status.Should().NotBe(ActivationCodeStatus.Valid);
            result.UserId.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidateActivation_DisabledUser_AnswersInvalid()
        {
            var service = CreateService();
            var user = AddUser("u1", active: false, UserLifecycleStatus.PendingVerification, isVerified: false);
            var code = await IssueActivationAsync(service, user);
            user.Status = UserLifecycleStatus.Disabled;

            var result = await ValidateAsync(service, code, "u1");

            result.Status.Should().Be(ActivationCodeStatus.Invalid);
            result.UserId.Should().BeEmpty();
        }
    }
}
