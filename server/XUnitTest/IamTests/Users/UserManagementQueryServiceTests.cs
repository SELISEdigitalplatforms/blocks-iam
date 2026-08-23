using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;
using Moq;

namespace XUnitTest.IamTests.Users
{
    public class UserManagementQueryServiceTests : IDisposable
    {
        private readonly Mock<IUserRepository> _repo = new();

        private UserManagementQueryService Create() =>
            new(NullLogger<UserManagementQueryService>.Instance, _repo.Object);

        private static void InstallContext(string userId = "user-1", string orgId = "default")
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: userId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        [Fact]
        public async Task IsUserAvailable_TrueWhenNoUser()
        {
            _repo.Setup(r => r.GetUserByEmailAsync("a@b.com")).ReturnsAsync((User)null!);
            var result = await Create().IsUserAvailableAsync(new IsEmailAvailableRequest { Email = "A@B.com" });
            result.Should().BeTrue();
        }

        [Fact]
        public async Task IsUserExist_ReturnsEmpty_WhenNoUser()
        {
            _repo.Setup(r => r.GetUserByEmailAsync("a@b.com")).ReturnsAsync((User)null!);

            var result = await Create().IsUserExistAsync("a@b.com");

            result.UserId.Should().BeNull();
            result.OrganizationIds.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUsers_MapsDataAndReturnsCount()
        {
            var accounts = new List<GetAccounts>
            {
                new() { ItemId = "u1", Email = "u1@e.com", FirstName = "A" },
                new() { ItemId = "u2", Email = "u2@e.com", FirstName = "B" },
            }.AsQueryable();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((accounts, 2L));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(2);
            result.Data.Should().HaveCount(2);
            result.Data.First()["itemId"].Should().Be("u1");
        }

        [Fact]
        public async Task GetUsers_HandlesNullData()
        {
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync(((IQueryable<GetAccounts>?)null, 0L));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAccount_ReturnsMappedData_WhenUserFound()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("user-1"))
                .ReturnsAsync(new GetAccounts { ItemId = "user-1", Email = "u@e.com", OrganizationIds = new List<string> { "default" } });

            var result = await Create().GetAccountAsync();

            result.Data.Should().NotBeNull();
            result.Data!["itemId"].Should().Be("user-1");
        }

        [Fact]
        public async Task GetAccount_ReturnsNullData_WhenUserMissing()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("user-1")).ReturnsAsync((GetAccounts)null!);

            var result = await Create().GetAccountAsync();

            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetUser_DefaultOrg_IncludesOrganizationsRolesAndPermissions()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u9"))
                .ReturnsAsync(new GetAccounts
                {
                    ItemId = "u9",
                    Email = "u9@e.com",
                    OrganizationIds = new List<string> { "default" },
                    Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } },
                    Permissions = new Dictionary<string, List<string>> { { "default", new List<string> { "read" } } }
                });

            var result = await Create().GetUserAsync("u9", "default");

            result.Data.Should().ContainKey("OrganizationsRoles");
            result.Data.Should().ContainKey("OrganizationsPermissions");
        }

        [Fact]
        public async Task GetUser_NonDefaultOrg_DoesNotIncludeOrganizationsRoles()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u9"))
                .ReturnsAsync(new GetAccounts { ItemId = "u9", Email = "u9@e.com" });

            var result = await Create().GetUserAsync("u9", "org-42");

            result.Data.Should().NotContainKey("OrganizationsRoles");
        }

        // =====================================================================================
        // #427 — lockout state exposure. Phase 1 of 2; the FE phase is built against these keys.
        // =====================================================================================

        /// <summary>Clock the service reads once per response, so exact-equality cases are arrangeable.</summary>
        private sealed class FixedClock(DateTime utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
        }

        /// <summary>A clock that fails the test if it is read at all.</summary>
        private sealed class ThrowingClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() =>
                throw new InvalidOperationException("the clock must not be read when there is nothing to map");
        }

        private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

        private UserManagementQueryService CreateAt(DateTime utcNow) =>
            new(NullLogger<UserManagementQueryService>.Instance, _repo.Object, new FixedClock(utcNow));

        private static GetAccounts Account(string id, DateTime? lockoutUntilUtc) => new()
        {
            ItemId = id,
            Email = id + "@e.com",
            OrganizationIds = new List<string> { "default" },
            LockoutUntilUtc = lockoutUntilUtc
        };

        [Fact]
        public async Task GetUser_FutureLockout_ReportsLockedOut()
        {
            // H1
            InstallContext();
            var until = Now.AddHours(1);
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u1")).ReturnsAsync(Account("u1", until));

            var result = await CreateAt(Now).GetUserAsync("u1", "default");

            result.Data!["lockoutUntilUtc"].Should().Be(until);
            result.Data["isLockedOut"].Should().Be(true);
        }

        [Fact]
        public async Task GetUser_NeverLockedOut_KeyIsPresentAndNull()
        {
            // H2. Presence is asserted separately from the value: a dropped key breaks the Phase 2
            // contract exactly as badly as a wrong one, and `Should().BeNull()` alone passes for a
            // key that was never written.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u2")).ReturnsAsync(Account("u2", null));

            var result = await CreateAt(Now).GetUserAsync("u2", "default");

            result.Data.Should().ContainKey("lockoutUntilUtc");
            result.Data!["lockoutUntilUtc"].Should().BeNull();
            result.Data["isLockedOut"].Should().Be(false);
        }

        [Fact]
        public async Task GetUser_ExpiredLockout_ReturnsInstantVerbatimButNotLockedOut()
        {
            // H3. The raw instant is still reported even though the window has passed - the field is
            // not cleared until a login attempt, and the contract says "verbatim, no transformation".
            InstallContext();
            var until = Now.AddHours(-1);
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u3")).ReturnsAsync(Account("u3", until));

            var result = await CreateAt(Now).GetUserAsync("u3", "default");

            result.Data!["lockoutUntilUtc"].Should().Be(until);
            result.Data["isLockedOut"].Should().Be(false);
        }

        [Fact]
        public async Task GetUsers_EachItemComputedIndependently()
        {
            // H4. Three states in ONE response: a mapper that computes once and reuses the answer,
            // or that reads the first item's value, fails here and nowhere else.
            var future = Now.AddHours(1);
            var past = Now.AddHours(-1);
            var accounts = new List<GetAccounts>
            {
                Account("locked", future), Account("never", null), Account("expired", past)
            }.AsQueryable();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((accounts, 3L));

            var items = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.ToList();

            items[0]["lockoutUntilUtc"].Should().Be(future);
            items[0]["isLockedOut"].Should().Be(true);
            items[1]["lockoutUntilUtc"].Should().BeNull();
            items[1]["isLockedOut"].Should().Be(false);
            items[2]["lockoutUntilUtc"].Should().Be(past);
            items[2]["isLockedOut"].Should().Be(false);
        }

        [Fact]
        public async Task GetUser_LockoutExactlyNow_IsNotLockedOut()
        {
            // C1. Strictly greater-than, matching the authentication check. Only arrangeable because
            // the clock is injected: against a live DateTime.UtcNow a test can never hit equality,
            // and both > and >= would return false - passing with the predicate wrong.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u4")).ReturnsAsync(Account("u4", Now));

            var result = await CreateAt(Now).GetUserAsync("u4", "default");

            result.Data!["isLockedOut"].Should().Be(false);
        }

        [Fact]
        public async Task GetUser_OneTickAfterNow_IsLockedOut()
        {
            // The other side of the C1 boundary, so "always false" cannot pass.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u5"))
                .ReturnsAsync(Account("u5", Now.AddTicks(1)));

            var result = await CreateAt(Now).GetUserAsync("u5", "default");

            result.Data!["isLockedOut"].Should().Be(true);
        }

        [Fact]
        public async Task GetUsers_EmptyResult_IsUnchangedAndErrorFree()
        {
            // C2. Does not claim to prove "no lockout computation" - the helper is private and
            // there are no items to map regardless.
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            var result = await CreateAt(Now).GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUsers_EmptyResult_AttemptsNoLockoutComputation()
        {
            // C2's literal clause: "no lockout computation is attempted". Asserting the output alone
            // could not show that - an implementation that computed and discarded would pass. A clock
            // that throws when touched is what actually demonstrates it.
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            var svc = new UserManagementQueryService(
                NullLogger<UserManagementQueryService>.Instance, _repo.Object, new ThrowingClock());

            var result = await svc.GetUsersAsync(new GetUsersRequest());

            result.Data.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUser_MissingUser_AttemptsNoLockoutComputation()
        {
            // C4's second, genuinely satisfiable half. (Its first half - "the existing not-found
            // error" - describes behaviour this endpoint does not have; see the PR.)
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("nope")).ReturnsAsync((GetAccounts)null!);

            var svc = new UserManagementQueryService(
                NullLogger<UserManagementQueryService>.Instance, _repo.Object, new ThrowingClock());

            // Non-default org: the default-org path dereferences null before returning, which is the
            // pre-existing defect this phase deliberately leaves alone.
            var result = await svc.GetUserAsync("nope", "org-42");

            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetUser_MissingUser_DefaultOrg_ThrowsToday_KnownPreExistingDefect()
        {
            // Pins ACTUAL behaviour, not desired. C4 says a non-existent id returns "the existing
            // not-found error" - there is none. On the default-org path GetUserAsync builds a null
            // `data` and then dereferences it to add OrganizationsRoles, so the caller gets a 500.
            //
            // #427 deliberately does not fix this: introducing a 404 changes the endpoint's status
            // code and controller response type, which contradicts C6 and the ticket's out-of-scope
            // list. Asserted so a future fix has to flip this test deliberately rather than silently,
            // and so the C4 discrepancy is visible in the suite and not only in the PR.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("nope")).ReturnsAsync((GetAccounts)null!);

            var act = async () => await CreateAt(Now).GetUserAsync("nope", "default");

            await act.Should().ThrowAsync<NullReferenceException>();
        }

        [Fact]
        public async Task GetUser_OutOfOrg_StaysEmptyAndLeaksNoLockoutState()
        {
            // The cross-org guard. The pre-existing test only checked that OrganizationsRoles was
            // absent, which would still pass if the lockout keys leaked - so this asserts the
            // response is EXACTLY empty for a caller outside the user's organizations.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u6"))
                .ReturnsAsync(new GetAccounts
                {
                    ItemId = "u6",
                    Email = "u6@e.com",
                    OrganizationIds = new List<string> { "org-1" },
                    LockoutUntilUtc = Now.AddHours(1)
                });

            var result = await CreateAt(Now).GetUserAsync("u6", "org-42");

            result.Data.Should().BeEmpty();
            result.Data.Should().NotContainKey("lockoutUntilUtc");
            result.Data.Should().NotContainKey("isLockedOut");
        }

        [Fact]
        public async Task GetUser_ReturnsWhateverTheRepositoryReturned()
        {
            // C5. Proves there is no service-level caching of lockout state between calls. It does
            // NOT prove the absence of locking - an implementation adding one would pass too.
            InstallContext();
            _repo.SetupSequence(r => r.GetUserByIdAsync<GetAccounts>("u7"))
                .ReturnsAsync(Account("u7", Now.AddHours(1)))
                .ReturnsAsync(Account("u7", null));

            var svc = CreateAt(Now);
            (await svc.GetUserAsync("u7", "default")).Data!["isLockedOut"].Should().Be(true);
            (await svc.GetUserAsync("u7", "default")).Data!["isLockedOut"].Should().Be(false);
        }

        [Fact]
        public async Task GetAccount_MeEndpoint_DoesNotGainLockoutFields()
        {
            // Scope boundary: GET /me uses MapToSingleAccountFields and is out of scope, so adding
            // the fields there too would be an unrequested API change.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("user-1"))
                .ReturnsAsync(Account("user-1", Now.AddHours(1)));

            var result = await CreateAt(Now).GetAccountAsync();

            // The COMPLETE key set, not merely the absence of the two new ones: this endpoint is out
            // of scope, so any drift in its shape - a gained field or a lost one - should fail here.
            result.Data!.Keys.Should().BeEquivalentTo(new[]
            {
                "itemId", "createdDate", "lastUpdatedDate", "language", "salutation", "firstName",
                "lastName", "email", "phoneNumber", "roles", "permissions", "active", "status",
                "isVerified", "profileImageUrl", "mfaEnabled", "isMfaVerified", "userMfaType",
                "externalIdentities", "attributes", "logInCount", "lastLoggedInTime",
                "lastLoggedInDeviceInfo", "organizationId"
            });
        }

        [Fact]
        public async Task GetUsers_ListShape_IsExactlyTheExistingKeysPlusTwo()
        {
            // H6 + C6 for the list. Asserts the COMPLETE dictionary with distinctive values, not
            // just the key names: a key-set comparison alone would pass if `active` silently became
            // the string "true".
            var account = new GetAccounts
            {
                ItemId = "u8", FirstName = "Ada", LastName = "Lovelace", Email = "ada@e.com",
                UserName = "ada", Active = true, IsVerified = true, ProfileImageUrl = "http://img",
                MfaEnabled = true, LogInCount = 7, LastLoggedInTime = Now.AddDays(-1),
                CreatedDate = Now.AddDays(-30), LockoutUntilUtc = null,
                Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } }
            };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            item.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["itemId"] = "u8",
                ["firstName"] = "Ada",
                ["lastName"] = "Lovelace",
                ["email"] = "ada@e.com",
                ["userName"] = "ada",
                ["active"] = true,
                ["status"] = account.Status,
                ["isVerified"] = true,
                ["profileImageUrl"] = "http://img",
                ["mfaEnabled"] = true,
                ["lastLoggedInTime"] = account.LastLoggedInTime,
                ["loginCount"] = 7,
                ["createdDate"] = account.CreatedDate,
                ["roles"] = account.Roles,
                ["lockoutUntilUtc"] = null,
                ["isLockedOut"] = false
            });
        }

        [Fact]
        public async Task GetUser_DetailShape_IsExactlyTheExistingKeysPlusTwo()
        {
            // H6 + C6 for the detail endpoint, including the default-org extras.
            InstallContext();
            var account = new GetAccounts
            {
                ItemId = "u9", Language = "en", Salutation = "Ms", FirstName = "Grace",
                LastName = "Hopper", Email = "grace@e.com", PhoneNumber = "123",
                Active = true, IsVerified = true, ProfileImageUrl = "http://img2",
                MfaEnabled = true, IsMfaVerified = true, LogInCount = 3,
                LastLoggedInTime = Now.AddDays(-2), LastLoggedInDeviceInfo = "cli",
                CreatedDate = Now.AddDays(-10), LastUpdatedDate = Now.AddDays(-1),
                OrganizationIds = new List<string> { "default" },
                LockoutUntilUtc = Now.AddHours(2),
                Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } },
                Permissions = new Dictionary<string, List<string>> { { "default", new List<string> { "read" } } }
            };
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u9")).ReturnsAsync(account);

            var data = (await CreateAt(Now).GetUserAsync("u9", "default")).Data;

            data.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["itemId"] = "u9",
                ["createdDate"] = account.CreatedDate,
                ["lastUpdatedDate"] = account.LastUpdatedDate,
                ["language"] = "en",
                ["salutation"] = "Ms",
                ["firstName"] = "Grace",
                ["lastName"] = "Hopper",
                ["email"] = "grace@e.com",
                ["phoneNumber"] = "123",
                ["roles"] = account.Roles["default"],
                ["permissions"] = account.Permissions["default"],
                ["active"] = true,
                ["status"] = account.Status,
                ["isVerified"] = true,
                ["profileImageUrl"] = "http://img2",
                ["mfaEnabled"] = true,
                ["isMfaVerified"] = true,
                ["userMfaType"] = account.UserMfaType,
                ["externalIdentities"] = account.ExternalIdentities,
                ["attributes"] = account.Attributes,
                ["logInCount"] = 3,
                ["lastLoggedInTime"] = account.LastLoggedInTime,
                ["lastLoggedInDeviceInfo"] = "cli",
                ["organizationIds"] = account.OrganizationIds,
                ["lockoutUntilUtc"] = account.LockoutUntilUtc,
                ["isLockedOut"] = true,
                ["OrganizationsRoles"] = account.Roles,
                ["OrganizationsPermissions"] = account.Permissions
            });
        }

        [Fact]
        public void GetAccounts_HydratesLockoutUntilUtc_AcrossTheBsonBoundary()
        {
            // The service tests all hand GetAccounts straight to a mock, so none of them prove a
            // PERSISTED value reaches the API. Both repository paths project with
            // Builders<User>.Projection.As<GetAccounts>(), which is a whole-document deserialise, so
            // this round-trips real BSON rather than going through a mock that would prove nothing.
            var until = new DateTime(2026, 8, 19, 15, 0, 0, DateTimeKind.Utc);
            var user = new User { ItemId = "u10", Email = "u10@e.com", LockoutUntilUtc = until };

            var bson = user.ToBsonDocument();
            var projected = BsonSerializer.Deserialize<GetAccounts>(bson);

            projected.ItemId.Should().Be("u10");
            projected.LockoutUntilUtc.Should().Be(until);
        }

        [Fact]
        public void Service_IsConstructable_WithoutATimeProviderRegistration()
        {
            // #427 added a TimeProvider dependency. The danger is activation, not registration: this
            // service is registered from Authentication.DomainService's RegisterAllServices - the
            // root Api/Program.cs actually calls - NOT from Iam.DomainService's
            // RegisterSharedServices, which only its own unit tests use. Had TimeProvider been a
            // required parameter registered in the wrong root, the API would have failed to build
            // the controller at runtime while every service-level test here stayed green.
            //
            // Deliberately NOT resolved from the full RegisterAllServices graph: that needs
            // host-level Genesis services (IDbContextProvider and friends) which no unit test has.
            // This asserts the thing that is actually in doubt - that MS DI can activate the service
            // with NO TimeProvider registered anywhere, via the optional-parameter fallback.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new Mock<IUserRepository>().Object);
            services.AddSingleton<IUserManagementQueryService, UserManagementQueryService>();

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IUserManagementQueryService>().Should().NotBeNull();
        }

        [Fact]
        public void LockoutFields_SerializeToTheWireContractPhaseTwoExpects()
        {
            // Every other test here inspects a Dictionary<string, object>, all of which pass even if
            // HTTP serialisation drops the null key or formats the timestamp wrongly - and Phase 2 is
            // built against the JSON, not the dictionary.
            //
            // Options are RESOLVED from a real MVC registration rather than constructed, so a
            // configured naming policy or converter would show up here. Measured on this stack:
            // DefaultIgnoreCondition = Never (so a null value keeps its key), camelCase property
            // policy, no converters. Residual limit, stated rather than papered over: the API layers
            // its MVC setup through Genesis's ConfigureApi, which needs host services this test does
            // not have - so if Genesis ever added a converter or a DictionaryKeyPolicy, only a hosted
            // response test would catch it.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddControllers();
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

            var lockedJson = JsonSerializer.Serialize(
                new Dictionary<string, object?> { ["lockoutUntilUtc"] = new DateTime(2026, 8, 19, 15, 0, 0, DateTimeKind.Utc) },
                options);
            lockedJson.Should().Contain("2026-08-19T15:00:00Z");

            var neverJson = JsonSerializer.Serialize(
                new Dictionary<string, object?> { ["lockoutUntilUtc"] = null }, options);
            neverJson.Should().Contain("\"lockoutUntilUtc\":null",
                "a dropped null key would silently break the Phase 2 contract");
        }
    }
}
