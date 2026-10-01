using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
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
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        private UserManagementQueryService Create() =>
            new(NullLogger<UserManagementQueryService>.Instance, _repo.Object, identityAccessManagementRepository: _iam.Object);

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
            InstallContext();
            var accounts = new List<GetAccounts>
            {
                new() { ItemId = "u1", Email = "u1@e.com", FirstName = "A" },
                new() { ItemId = "u2", Email = "u2@e.com", FirstName = "B" },
            }.AsQueryable();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((accounts, 2L));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(2);
            result.Data.Should().HaveCount(2);
            result.Data.First()["itemId"].Should().Be("u1");
        }

        [Fact]
        public async Task GetUsers_HandlesNullData()
        {
            InstallContext();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync(((IQueryable<GetAccounts>?)null, 0L));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
        }

        [Fact] // C1 -- a token with no organization is answered without touching the repository
        public async Task GetUsers_WithNoOrganizationInTheToken_ReturnsNothingAndNeverQueries()
        {
            InstallContext(orgId: string.Empty);
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ThrowsAsync(new InvalidOperationException("the repository must not be reached"));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
            _repo.Verify(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()), Times.Never);
            _iam.Verify(r => r.GetTenantConfigurationAsync(), Times.Never);
        }

        [Fact] // H1 -- a "default" caller that requests nothing reaches the repository tenant-wide
        public async Task GetUsers_DefaultOrganisationWithNoFilter_AsksForEveryOrganization()
        {
            InstallContext();
            UserListScope? captured = null;
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .Callback<GetUsersRequest, UserListScope>((_, scope) => captured = scope)
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            await Create().GetUsersAsync(new GetUsersRequest());

            captured!.Kind.Should().Be(UserListScopeKind.AllOrganizations);
        }

        [Fact] // H3 -- the requested organizations reach the repository as a union scope
        public async Task GetUsers_DefaultOrganisationWithSeveralOrganizations_PassesThemThrough()
        {
            InstallContext();
            UserListScope? captured = null;
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .Callback<GetUsersRequest, UserListScope>((_, scope) => captured = scope)
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            await Create().GetUsersAsync(new GetUsersRequest
            {
                Filter = new GetUsersFilter { OrganizationIds = ["org-a", "org-b"] }
            });

            captured!.Kind.Should().Be(UserListScopeKind.Organizations);
            captured.OrganizationIds.Should().Equal("org-a", "org-b");
        }

        [Fact] // C2 -- a non-default caller cannot widen scope from the payload
        public async Task GetUsers_NonDefaultOrganisation_IgnoresTheRequestedOrganizations()
        {
            InstallContext(orgId: "org-a");
            UserListScope? captured = null;
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .Callback<GetUsersRequest, UserListScope>((_, scope) => captured = scope)
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            await Create().GetUsersAsync(new GetUsersRequest
            {
                Filter = new GetUsersFilter { OrganizationIds = ["org-b"] }
            });

            captured!.OrganizationIds.Should().Equal("org-a");
        }

        [Fact]
        public async Task GetUsers_DeniedScopeWithRoles_ReturnsNothingWithoutTenantConfigOrRepository()
        {
            InstallContext(orgId: " ");
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ThrowsAsync(new InvalidOperationException("the repository must not be reached"));
            _iam.Setup(r => r.GetTenantConfigurationAsync())
                .ThrowsAsync(new InvalidOperationException("tenant config must not be fetched"));

            var result = await Create().GetUsersAsync(new GetUsersRequest
            {
                Filter = new GetUsersFilter { Roles = ["admin"] }
            });

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
            result.Errors.Should().BeNull();
            _repo.Verify(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()), Times.Never);
            _iam.Verify(r => r.GetTenantConfigurationAsync(), Times.Never);
        }

        [Fact]
        public async Task GetUsers_AllOrganizationsWithRolesAndMultiOrgEnabled_ReturnsOrganizationRequiredError()
        {
            InstallContext();
            _iam.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ThrowsAsync(new InvalidOperationException("the repository must not be reached"));

            var result = await Create().GetUsersAsync(new GetUsersRequest
            {
                Filter = new GetUsersFilter { Roles = ["admin"] }
            });

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
            result.Errors.Should().ContainSingle();
            result.Errors!["OrganizationIds"].Should().Be("OrganizationIds_Required_For_Role_Filter");
            _repo.Verify(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()), Times.Never);
        }

        [Fact]
        public async Task GetUsers_AllOrganizationsWithRolesAndMultiOrgDisabled_SubstitutesDefaultOrganizationScope()
        {
            InstallContext();
            _iam.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });
            UserListScope? captured = null;
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .Callback<GetUsersRequest, UserListScope>((_, scope) => captured = scope)
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            await Create().GetUsersAsync(new GetUsersRequest
            {
                Filter = new GetUsersFilter { Roles = ["manager"] }
            });

            captured!.Kind.Should().Be(UserListScopeKind.Organizations);
            captured.OrganizationIds.Should().Equal("default");
        }

        [Fact]
        public async Task GetUsers_AllOrganizationsWithoutRoles_DoesNotFetchTenantConfig()
        {
            InstallContext();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(
                    It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            await Create().GetUsersAsync(new GetUsersRequest
            {
                Filter = new GetUsersFilter { Roles = [] }
            });

            _iam.Verify(r => r.GetTenantConfigurationAsync(), Times.Never);
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
            new(NullLogger<UserManagementQueryService>.Instance, _repo.Object, new FixedClock(utcNow), _iam.Object);

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
        public async Task GetUsers_CarriesNoLockoutState_WhateverTheStoredValue()
        {
            InstallContext();
            // The list no longer reports lockout. Three stored states - future, never, expired - so
            // a mapper that reinstated the keys for only one of them still fails here.
            var accounts = new List<GetAccounts>
            {
                Account("locked", Now.AddHours(1)), Account("never", null), Account("expired", Now.AddHours(-1))
            }.AsQueryable();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((accounts, 3L));

            var items = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.ToList();

            items.Should().HaveCount(3);
            items.Should().OnlyContain(item =>
                !item.ContainsKey("lockoutUntilUtc") && !item.ContainsKey("isLockedOut"));
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
            InstallContext();
            // C2. Does not claim to prove "no lockout computation" - the helper is private and
            // there are no items to map regardless.
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            var result = await CreateAt(Now).GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUsers_EmptyResult_AttemptsNoLockoutComputation()
        {
            InstallContext();
            // C2's literal clause: "no lockout computation is attempted". Asserting the output alone
            // could not show that - an implementation that computed and discarded would pass. A clock
            // that throws when touched is what actually demonstrates it.
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((Enumerable.Empty<GetAccounts>().AsQueryable(), 0L));

            var svc = new UserManagementQueryService(
                NullLogger<UserManagementQueryService>.Instance, _repo.Object, new ThrowingClock(), _iam.Object);

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
                NullLogger<UserManagementQueryService>.Instance, _repo.Object, new ThrowingClock(), _iam.Object);

            // Non-default org: the default-org path dereferences null before returning, which is the
            // pre-existing defect this phase deliberately leaves alone.
            var result = await svc.GetUserAsync("nope", "org-42");

            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetUser_MissingUser_DefaultOrg_ReturnsNullWithoutThrowing()
        {
            // Deliberately flipped from the earlier assertion that this threw. The default-org path
            // used to build a null `data` and then dereference it to add OrganizationsRoles, so the
            // caller got a 500; the guard added with the field configuration skips that block unless
            // there is a populated dictionary to add to.
            //
            // Still not a 404: introducing one changes the endpoint's status code and controller
            // response type, which is a separate decision.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("nope")).ReturnsAsync((GetAccounts)null!);

            var result = await CreateAt(Now).GetUserAsync("nope", "default");

            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetUser_OutOfOrg_DefaultOrg_GainsNoOrganizationWideRolesOrPermissions()
        {
            // The same guard from the other side: an empty dictionary means the caller is outside
            // the user's organizations, and must not be handed the unscoped roles and permissions
            // dictionaries for every organization the user belongs to.
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u12"))
                .ReturnsAsync(new GetAccounts
                {
                    ItemId = "u12",
                    Email = "u12@e.com",
                    OrganizationIds = new List<string> { "org-1" },
                    Roles = new Dictionary<string, List<string>> { { "org-1", new List<string> { "admin" } } }
                });

            var result = await CreateAt(Now).GetUserAsync("u12", "org-42");

            result.Data.Should().BeEmpty();
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

            // The COMPLETE key set, not merely the absence of the lockout pair: with no
            // configuration stored this endpoint's shape is unchanged, so any drift - a gained
            // field or a lost one - should fail here.
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
        public async Task GetUsers_ListShape_IsTheDefaultFieldSet()
        {
            InstallContext();
            // The COMPLETE dictionary with distinctive values, not just the key names: a key-set
            // comparison alone would pass if `active` silently became the string "true". The
            // account below sets Status, MfaEnabled and Roles to non-default values on purpose, so
            // reinstating any of those five removed keys fails here rather than silently widening
            // the payload again.
            var account = new GetAccounts
            {
                ItemId = "u8", FirstName = "Ada", LastName = "Lovelace", Email = "ada@e.com",
                UserName = "ada", Active = true, IsVerified = true, ProfileImageUrl = "http://img",
                MfaEnabled = true, LogInCount = 7, LastLoggedInTime = Now.AddDays(-1),
                CreatedDate = Now.AddDays(-30), LockoutUntilUtc = Now.AddHours(1),
                Status = UserLifecycleStatus.Suspended,
                Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } }
            };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            // No configuration stored, so DefaultListFields applies. The account sets Status,
            // MfaEnabled, Roles and LockoutUntilUtc to non-default values on purpose: the mapper
            // builds those keys so they can be configured, and the default must still drop them.
            item.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["itemId"] = "u8",
                ["firstName"] = "Ada",
                ["lastName"] = "Lovelace",
                ["email"] = "ada@e.com",
                ["userName"] = "ada",
                ["active"] = true,
                ["isVerified"] = true,
                ["profileImageUrl"] = "http://img",
                ["lastLoggedInTime"] = account.LastLoggedInTime,
                ["loginCount"] = 7,
                ["createdDate"] = account.CreatedDate
            });
        }

        [Fact]
        public async Task GetUsers_ConfiguredFields_CanAddBeyondTheDefault()
        {
            InstallContext();
            // The other direction: a tenant that wants the role and lockout columns names them, and
            // gets keys the default omits. Proves the default is a starting point, not a ceiling.
            var account = new GetAccounts
            {
                ItemId = "u13", Email = "e@e.com", LockoutUntilUtc = Now.AddHours(1),
                Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } }
            };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));
            _repo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { UserListFields = ["email", "roles", "isLockedOut"] });

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            item.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["itemId"] = "u13",
                ["email"] = "e@e.com",
                ["roles"] = account.Roles,
                ["isLockedOut"] = true
            });
        }

        [Fact]
        public async Task GetUsers_ConfiguredFields_NarrowTheResponse()
        {
            InstallContext();
            var account = new GetAccounts
            {
                ItemId = "u10", FirstName = "Ada", Email = "ada@e.com", Active = true,
                CreatedDate = Now.AddDays(-3)
            };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));
            _repo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { UserListFields = ["email", "active"] });

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            // itemId always survives - it is the row key - and createdDate, a default field, is
            // dropped because an explicit configuration replaces the default rather than adding to it.
            item.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["itemId"] = "u10",
                ["email"] = "ada@e.com",
                ["active"] = true
            });
        }

        [Fact]
        public async Task GetUsers_ConfiguredBlankEntries_FallBackToTheDefault()
        {
            InstallContext();
            // The configuration is a hand-editable Mongo document. A list of blanks is a mistake,
            // not an instruction to return almost nothing, so it is treated as absent.
            var account = new GetAccounts
            {
                ItemId = "u15", FirstName = "Ada", Email = "ada@e.com", Active = true,
                CreatedDate = Now.AddDays(-1)
            };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));
            _repo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { UserListFields = ["", "   ", null!] });

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            item.Keys.Should().BeEquivalentTo(UserFieldPolicy.DefaultListFields);
        }

        [Fact]
        public async Task GetUsers_ConfiguredNames_AreTrimmedAndCaseInsensitive()
        {
            InstallContext();
            // Same reason: a name typed with stray whitespace or the wrong casing should still work.
            var account = new GetAccounts { ItemId = "u16", Email = "ada@e.com", Active = true };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));
            _repo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { UserListFields = ["  Email  ", "ACTIVE"] });

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            item.Keys.Should().BeEquivalentTo(["itemId", "email", "active"]);
        }

        [Fact]
        public async Task GetUsers_ConfiguredUnknownField_CannotWidenTheResponse()
        {
            InstallContext();
            // The mapper is the ceiling. "password" is never built, and never projected into
            // GetAccounts either, so naming it changes nothing - configuration narrows only.
            var account = new GetAccounts { ItemId = "u11", Email = "e@e.com", Active = true };
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>(), It.IsAny<UserListScope>()))
                .ReturnsAsync((new[] { account }.AsQueryable(), 1L));
            _repo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { UserListFields = ["password", "securityStamp", "active"] });

            var item = (await CreateAt(Now).GetUsersAsync(new GetUsersRequest())).Data.Single();

            item.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["itemId"] = "u11",
                ["active"] = true
            });
        }

        [Fact]
        public async Task GetUser_DetailShape_IsUnchangedByDefault()
        {
            // The detail endpoint's complete shape, including the default-org extras. Nothing was
            // asked of this endpoint, so with no configuration stored it answers exactly as before.
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
        public async Task GetUser_ConfiguredFields_NarrowTheDetailResponse()
        {
            InstallContext();
            // The detail endpoint honours UserDetailFields the same way the list honours
            // UserListFields. Queried in the user's own organization, so the cross-org guard is not
            // what empties the response - the configuration is.
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u14")).ReturnsAsync(new GetAccounts
            {
                ItemId = "u14", Email = "e@e.com", PhoneNumber = "123", FirstName = "Grace",
                OrganizationIds = new List<string> { "org-1" }
            });
            _repo.Setup(r => r.GetIamConfigurationAsync())
                .ReturnsAsync(new IamConfiguration { UserDetailFields = ["firstName"] });

            var data = (await CreateAt(Now).GetUserAsync("u14", "org-1")).Data;

            data!.Keys.Should().BeEquivalentTo(["itemId", "firstName"]);
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
