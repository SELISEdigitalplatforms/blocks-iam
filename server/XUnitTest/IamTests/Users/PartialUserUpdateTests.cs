using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// Acceptance tests for SPEC13 — sparse user update and the self-service model.
    /// <para>
    /// The contract these guard: a caller sends an id plus the fields it wants changed, and nothing
    /// else on the user moves. Before this, ten of twelve fields were destroyed by omission —
    /// omitting <c>roles</c> stripped them, omitting <c>mfaEnabled</c> disabled MFA — because a
    /// non-nullable property binds an absent JSON field to a default the service cannot tell apart
    /// from a deliberate value.
    /// </para>
    /// <para>
    /// H1/H2 are asserted per field rather than with one whole-object comparison: the bug was
    /// per-field, and a single object assertion would pass while one field still leaked.
    /// </para>
    /// </summary>
    public class PartialUserUpdateTests : IDisposable
    {
        private readonly Mock<IValidator<CreateUserRequest>> _createValidator = new();
        private readonly Mock<IValidator<UpdateUserRequest>> _updateValidator = new();
        private readonly Mock<IValidator<UpdateMyAccountRequest>> _myAccountValidator = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();
        private readonly Mock<IResourceRepository> _resourceRepo = new();

        private const string ActorId = "actor-1";

        public PartialUserUpdateTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>())).Returns(new ValidationResult());
            _myAccountValidator.Setup(v => v.Validate(It.IsAny<UpdateMyAccountRequest>())).Returns(new ValidationResult());
            _message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<UserMutationEvent>>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            _resourceRepo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration());
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);
        }

        private static void InstallContext(string userId = ActorId, string orgId = "default") =>
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: userId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

        public void Dispose()
        {
            BlocksContext.SetContext(null!);
            GC.SuppressFinalize(this);
        }

        private UserManagementMutationService Create() =>
            new(NullLogger<UserManagementMutationService>.Instance, _createValidator.Object, _updateValidator.Object,
                _myAccountValidator.Object, _iam.Object, _userRepo.Object, _message.Object, _cache.Object,
                _tenants.Object, _activity.Object, null, _resourceRepo.Object, null);

        private static Dictionary<string, object> Bind(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        /// <summary>A fully populated user, so every field has something to lose.</summary>
        private User Seed()
        {
            var user = new User
            {
                ItemId = "u1",
                Salutation = "Dr",
                FirstName = "Ada",
                LastName = "Lovelace",
                PhoneNumber = "+880170",
                Language = "en-US",
                ProfileImageUrl = "https://img/a.png",
                ProfileImageId = "img-1",
                Tags = new List<string> { "staff" },
                Attributes = new Dictionary<string, object> { { "plan", "pro" } },
                OrganizationIds = new List<string> { "default" },
                Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } },
                Permissions = new Dictionary<string, List<string>> { { "default", new List<string> { "iam::users" } } },
                MfaEnabled = true,
                UserMfaType = UserMfaType.TOTP,
                IsMfaVerified = true,
            };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            return user;
        }

        // ---------- H1: omission preserves, field by field ----------

        public static TheoryData<string> SettableFields() => new()
        {
            "Salutation", "FirstName", "LastName", "PhoneNumber",
            "Language", "ProfileImageUrl", "ProfileImageId", "Tags", "Attributes",
        };

        [Theory]
        [MemberData(nameof(SettableFields))]
        public async Task H1_FieldOmitted_IsLeftUnchanged(string field)
        {
            var user = Seed();
            var before = Snapshot(user);

            // Every field absent except one unrelated one, so the request is a real partial update.
            var request = new UpdateUserRequest { ItemId = "u1", OrganizationId = "default" };
            typeof(UpdateUserRequest).GetProperty(field)!.SetValue(request, null);
            request.FirstName = field == "FirstName" ? null : "Touched";

            var result = await Create().UpdateUserAsync(request);

            result.IsSuccess.Should().BeTrue();
            Snapshot(user)[field].Should().BeEquivalentTo(before[field], $"{field} was not mentioned in the request");
        }

        private static Dictionary<string, object?> Snapshot(User u) => new()
        {
            ["Salutation"] = u.Salutation,
            ["FirstName"] = u.FirstName,
            ["LastName"] = u.LastName,
            ["PhoneNumber"] = u.PhoneNumber,
            ["Language"] = u.Language,
            ["ProfileImageUrl"] = u.ProfileImageUrl,
            ["ProfileImageId"] = u.ProfileImageId,
            ["Tags"] = u.Tags.ToList(),
            ["Attributes"] = new Dictionary<string, object>(u.Attributes),
        };

        [Fact]
        public async Task H1_EmptyBody_ChangesNothingButAuditFields()
        {
            var user = Seed();
            var before = Snapshot(user);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default" });

            result.IsSuccess.Should().BeTrue();
            Snapshot(user).Should().BeEquivalentTo(before);
            user.MfaEnabled.Should().BeTrue();
            user.Roles["default"].Should().Equal("admin");
        }

        // ---------- H2 / H3 / H4: set, clear, explicit null ----------

        [Fact]
        public async Task H2_TwoFieldsSupplied_OnlyThoseTwoChange()
        {
            var user = Seed();

            var result = await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1",
                OrganizationId = "default",
                FirstName = "Ada Byron",
                PhoneNumber = "+880171",
            });

            result.IsSuccess.Should().BeTrue();
            user.FirstName.Should().Be("Ada Byron");
            user.PhoneNumber.Should().Be("+880171");
            user.Salutation.Should().Be("Dr");
            user.LastName.Should().Be("Lovelace");
            user.ProfileImageUrl.Should().Be("https://img/a.png");
            user.Tags.Should().Equal("staff");
            user.Attributes["plan"].Should().Be("pro");
        }

        [Fact]
        public async Task H3_EmptyFormsClearTheValue()
        {
            var user = Seed();

            await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1",
                OrganizationId = "default",
                PhoneNumber = "",
                Tags = new List<string>(),
                Attributes = new Dictionary<string, object>(),
            });

            user.PhoneNumber.Should().BeEmpty();
            user.Tags.Should().BeEmpty();
            user.Attributes.Should().BeEmpty();
            user.FirstName.Should().Be("Ada");
        }

        [Fact]
        public async Task H4_ExplicitNull_IsTreatedAsAbsent()
        {
            var user = Seed();

            await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1", OrganizationId = "default", LastName = null,
            });

            user.LastName.Should().Be("Lovelace");
        }

        [Fact]
        public async Task H5_SuccessfulUpdate_StampsUtcAndActor()
        {
            var user = Seed();
            var before = DateTime.UtcNow.AddSeconds(-1);

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", FirstName = "X" });

            // DateTime.Now would land an hour or more off UtcNow on any non-UTC host.
            user.LastUpdatedDate.Should().BeAfter(before).And.BeBefore(DateTime.UtcNow.AddSeconds(1));
            user.LastUpdatedBy.Should().Be(ActorId);
        }

        [Fact]
        public async Task H8_Language_IsNowSettable()
        {
            var user = Seed();

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", Language = "bn-BD" });

            user.Language.Should().Be("bn-BD");
        }

        // ---------- H6: self-service ----------

        [Fact]
        public async Task H6_UpdateMyAccount_UpdatesTheAuthenticatedUserOnly()
        {
            var user = new User { ItemId = ActorId, FirstName = "Ada", OrganizationIds = new List<string> { "default" } };
            _userRepo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync(user);

            var result = await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { FirstName = "Ada Self" });

            result.IsSuccess.Should().BeTrue();
            user.FirstName.Should().Be("Ada Self");
            _userRepo.Verify(r => r.GetUserByIdAsync(ActorId), Times.Once);
        }

        [Fact]
        public async Task H6_UpdateMyAccount_ObeysTheSameSparseRules()
        {
            var user = new User
            {
                ItemId = ActorId,
                FirstName = "Ada",
                LastName = "Lovelace",
                OrganizationIds = new List<string> { "default" },
            };
            _userRepo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync(user);

            await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { FirstName = "Ada Self" });

            user.LastName.Should().Be("Lovelace");
        }

        // ---------- H7 / C5 / C6: retired fields are inert ----------

        [Fact]
        public async Task H7_RetiredFieldsInBody_AreIgnoredAndDoNotFail()
        {
            var user = Seed();

            var result = await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1",
                OrganizationId = "default",
                FirstName = "Ada",
                UnmappedFields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    "{\"roles\":[\"viewer\"],\"permissions\":[\"read\"],\"mfaEnabled\":false,\"userMfaType\":0}"),
            });

            result.IsSuccess.Should().BeTrue();
            user.FirstName.Should().Be("Ada");
        }

        [Fact]
        public async Task C5_RolesAndPermissions_AreUntouchedByAProfileUpdate()
        {
            var user = Seed();

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", FirstName = "Ada" });

            user.Roles["default"].Should().Equal("admin");
            user.Permissions["default"].Should().Equal("iam::users");
            user.Roles.Keys.Should().Equal("default");
        }

        [Fact]
        public async Task C6_MfaState_IsUntouchedByAProfileUpdate()
        {
            var user = Seed();

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", FirstName = "Ada" });

            user.MfaEnabled.Should().BeTrue();
            user.UserMfaType.Should().Be(UserMfaType.TOTP);
            user.IsMfaVerified.Should().BeTrue();
        }

        // ---------- H7: the wire format itself ----------

        [Fact]
        public void H7_RetiredKeysOnTheWire_LandInUnmappedFields()
        {
            // Proves the [JsonExtensionData] capture works against a real body rather than a
            // hand-populated model. Without this, the warning path could silently never fire.
            var body = "{\"firstName\":\"Ada\",\"roles\":[\"viewer\"],\"permissions\":[\"read\"],\"mfaEnabled\":false}";

            var bound = JsonSerializer.Deserialize<UpdateUserRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            bound.FirstName.Should().Be("Ada");
            bound.UnmappedFields.Should().NotBeNull();
            bound.UnmappedFields!.Keys.Should().Contain(new[] { "roles", "permissions", "mfaEnabled" });
        }

        [Fact]
        public void H7_RetiredKeysAreNotProperties_SoTheyCannotBeWritten()
        {
            var properties = typeof(UpdateUserRequest).GetProperties().Select(p => p.Name).ToList();

            properties.Should().NotContain(new[] { "Roles", "Permissions", "MfaEnabled", "UserMfaType" });
            typeof(UpdateMyAccountRequest).GetProperties().Select(p => p.Name)
                .Should().NotContain(new[] { "ItemId", "OrganizationId", "Roles", "Permissions", "MfaEnabled", "UserMfaType", "Tags" });
        }

        [Fact]
        public void H6_SelfServiceBody_DiscardsAdminScopedKeys()
        {
            var body = "{\"firstName\":\"Ada\",\"itemId\":\"u2\",\"organizationId\":\"org-9\",\"roles\":[\"admin\"]}";

            var bound = JsonSerializer.Deserialize<UpdateMyAccountRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            bound.FirstName.Should().Be("Ada");
            bound.UnmappedFields!.Keys.Should().Contain(new[] { "itemId", "organizationId", "roles" });
        }

        // ---------- C1 / C2 / C7 / C8 / C9 ----------

        [Fact]
        public async Task C1_ValidationFailure_WritesNothing()
        {
            var user = Seed();
            _updateValidator.Setup(v => v.Validate(It.IsAny<UpdateUserRequest>()))
                .Returns(new ValidationResult(new[] { new ValidationFailure("FirstName", "Maximum character limit 150 exceeded") }));

            var result = await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1", OrganizationId = "default", FirstName = new string('x', 151), LastName = "Changed",
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("FirstName");
            user.LastName.Should().Be("Lovelace");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task C2_UnknownUser_ReturnsNotFoundAndWritesNothing()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync("missing")).ReturnsAsync((User)null!);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "missing", FirstName = "X" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
            _userRepo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task C2_UpdateMyAccount_UnknownUser_ReturnsNotFound()
        {
            _userRepo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync((User)null!);

            var result = await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { FirstName = "X" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ItemId");
        }

        [Fact]
        public async Task C7_ProfileUpdate_NeverGrantsOrganizationMembership()
        {
            InstallContext(orgId: "org-9");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string> { "default" } };
            _userRepo.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);

            await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", FirstName = "Ada" });

            user.OrganizationIds.Should().Equal("default");
        }

        [Fact]
        public async Task C8_RepositoryFailure_ReportsUnsuccessful()
        {
            Seed();
            _userRepo.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(false);

            var result = await Create().UpdateUserAsync(new UpdateUserRequest { ItemId = "u1", OrganizationId = "default", FirstName = "X" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task C9_Attributes_AreNormalizedNotStoredAsJsonElement()
        {
            var user = Seed();

            await Create().UpdateUserAsync(new UpdateUserRequest
            {
                ItemId = "u1",
                OrganizationId = "default",
                Attributes = Bind("{\"meta\":{\"region\":\"eu\"},\"seats\":10}"),
            });

            user.Attributes.Values.Should().NotContain(v => v is JsonElement);
            user.Attributes["seats"].Should().Be(10L);
            ((Dictionary<string, object>)user.Attributes["meta"])["region"].Should().Be("eu");
        }

        [Fact]
        public async Task C9_UpdateMyAccount_NormalizesAttributesToo()
        {
            var user = new User { ItemId = ActorId, OrganizationIds = new List<string> { "default" } };
            _userRepo.Setup(r => r.GetUserByIdAsync(ActorId)).ReturnsAsync(user);

            await Create().UpdateMyAccountAsync(new UpdateMyAccountRequest { Attributes = Bind("{\"plan\":\"pro\"}") });

            user.Attributes.Values.Should().NotContain(v => v is JsonElement);
            user.Attributes["plan"].Should().Be("pro");
        }
    }
}
