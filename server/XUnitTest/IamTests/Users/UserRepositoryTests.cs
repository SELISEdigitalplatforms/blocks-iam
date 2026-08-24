using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// Unit tests for <see cref="UserRepository"/>. Pure delegations are asserted against the mocked
    /// <see cref="IIdentityAccessManagementRepository"/>; the query methods that build filters and
    /// projections are exercised through mocked <see cref="IMongoCollection{T}"/> instances.
    /// </summary>
    public sealed class UserRepositoryTests : IDisposable
    {
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        public UserRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private UserRepository Sut() => new(_iam.Object);

        private static UserListScope Scope(params string[] organizationIds) =>
            new(UserListScopeKind.Organizations, organizationIds);

        private Mock<IMongoCollection<T>> Register<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            _iam.Setup(r => r.GetCollection<T>()).Returns(col.Object);
            return col;
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_Delegates()
        {
            _iam.Setup(r => r.CheckPasswordBlackListedAsync("pw")).ReturnsAsync(true);
            (await Sut().CheckPasswordBlackListedAsync("pw")).Should().BeTrue();
        }

        [Fact]
        public async Task CreateUserAsync_NormalizesAndInserts()
        {
            var col = Register<User>();
            var user = new User { ItemId = "u1", Email = " USER@X.COM ", UserName = " User " };
            (await Sut().CreateUserAsync(user)).Should().BeTrue();
            user.Email.Should().Be("user@x.com");
            user.UserName.Should().Be("user");
            col.Verify(c => c.InsertOneAsync(It.IsAny<User>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetIamConfigurationAsync_Delegates()
        {
            var cfg = new IamConfiguration { ItemId = ObjectId.GenerateNewId() };
            _iam.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(cfg);
            (await Sut().GetIamConfigurationAsync()).Should().BeSameAs(cfg);
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_NoUser_ReturnsEmpty()
        {
            Register<User>();
            (await Sut().GetPermissionsByResourcesAsync("missing")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_NoPermissions_ReturnsEmpty()
        {
            Register(new[] { new User { ItemId = "u1" } });
            (await Sut().GetPermissionsByResourcesAsync("u1")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_ReturnsProjectedPermissions()
        {
            Register(new[] { new User { ItemId = "u1", Permissions = new() { { "default", new List<string> { "res1" } } } } });
            var perms = Register<Permission>();
            MongoMock.SetupProjectedFind(perms, new List<GetUserPermission>
            {
                new() { ItemId = "p1", Resource = "res1", Name = "P1" }
            });
            var result = await Sut().GetPermissionsByResourcesAsync("u1");
            result.Should().ContainSingle(p => p.ItemId == "p1");
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ByList_ReturnsProjected()
        {
            var perms = Register<Permission>();
            MongoMock.SetupProjectedFind(perms, new List<GetUserPermission> { new() { ItemId = "p1", Resource = "res1" } });
            (await Sut().GetPermissionsByResourcesAsync(new List<string> { "res1" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsByRolesAsync_Empty_ReturnsEmpty()
        {
            (await Sut().GetPermissionsByRolesAsync(new List<string>())).Should().BeEmpty();
        }

        [Fact]
        public async Task GetPermissionsByRolesAsync_ReturnsProjected()
        {
            var perms = Register<Permission>();
            MongoMock.SetupProjectedFind(perms, new List<GetUserPermission> { new() { ItemId = "p1" } });
            (await Sut().GetPermissionsByRolesAsync(new List<string> { "admin" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ById_NoUser_ReturnsEmpty()
        {
            Register<User>();
            (await Sut().GetRolesBySlugsAsync("missing")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ById_ReturnsProjectedRoles()
        {
            Register(new[] { new User { ItemId = "u1", Roles = new() { { "default", new List<string> { "admin" } } } } });
            var roles = Register<Role>();
            MongoMock.SetupProjectedFind(roles, new List<GetUserRole> { new() { ItemId = "r1", Slug = "admin", Name = "Admin" } });
            (await Sut().GetRolesBySlugsAsync("u1")).Should().ContainSingle(r => r.Slug == "admin");
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ByList_ReturnsProjected()
        {
            var roles = Register<Role>();
            MongoMock.SetupProjectedFind(roles, new List<GetUserRole> { new() { ItemId = "r1", Slug = "admin" } });
            (await Sut().GetRolesBySlugsAsync(new List<string> { "admin" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetUserByEmailAsync_Delegates_WithNormalizedEmail()
        {
            var user = new User { ItemId = "u1" };
            _iam.Setup(r => r.GetUserByEmailAsync("user@x.com")).ReturnsAsync(user);
            (await Sut().GetUserByEmailAsync(" USER@X.COM ")).Should().BeSameAs(user);
        }

        [Fact]
        public async Task GetUserByIdAsync_Delegates()
        {
            var user = new User { ItemId = "u1" };
            _iam.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            (await Sut().GetUserByIdAsync("u1")).Should().BeSameAs(user);
        }

        [Fact]
        public async Task GetUserByIdAsyncGeneric_Delegates()
        {
            _iam.Setup(r => r.GetUserByIdAsync<string>("u1")).ReturnsAsync("value");
            (await Sut().GetUserByIdAsync<string>("u1")).Should().Be("value");
        }

        [Fact]
        public async Task GetUserByUserNameOrgIdAsync_WithOrg_ReturnsMatch()
        {
            Register(new[] { new User { ItemId = "u1", UserName = "user", OrganizationIds = new List<string> { "org1" } } });
            (await Sut().GetUserByUserNameOrgIdAsync("USER", "org1"))!.ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByUserNameOrgIdAsync_WithoutOrg_ReturnsMatch()
        {
            Register(new[] { new User { ItemId = "u1", UserName = "user" } });
            (await Sut().GetUserByUserNameOrgIdAsync("user"))!.ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUsersAsync_ProjectsAndReturnsCount()
        {
            var col = Register(new[] { new User { ItemId = "u1" }, new User { ItemId = "u2" } });
            MongoMock.SetupCount(col, 2);
            var query = new BaseGetsRequest<GetUsersFilter>
            {
                Page = 0,
                PageSize = 10,
                Sort = new BaseSortRequest { Property = "Email", IsDescending = false },
                Filter = new GetUsersFilter
                {
                    Name = "john",
                    Email = "john@x.com",
                    Status = new Status { Active = true },
                    Mfa = new MFA { Enabled = true },
                    JoinedOn = DateTime.UtcNow.AddDays(-10),
                    LastLogin = DateTime.UtcNow.AddDays(-1),
                    UserIds = new List<string> { "u1" },
                    OrganizationIds = ["org1"]
                }
            };
            var (items, count) = await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(query, Scope("org1"));
            count.Should().Be(2);
            items!.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetUsersAsync_NullFilter_UsesDefaults()
        {
            var col = Register(new[] { new User { ItemId = "u1" } });
            MongoMock.SetupCount(col, 1);
            // C7 -- a tenant-wide scope with a null filter leaves nothing to conjoin, so this is the
            // case that would build a zero-clause $and if the early return skipped its guard.
            var (items, count) = await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(
                new BaseGetsRequest<GetUsersFilter>(),
                new UserListScope(UserListScopeKind.AllOrganizations, []));
            count.Should().Be(1);
            items!.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetUsersAsync_InactiveAndMfaDisabledFilters()
        {
            var col = Register(new[] { new User { ItemId = "u1" } });
            MongoMock.SetupCount(col, 1);
            var query = new BaseGetsRequest<GetUsersFilter>
            {
                Filter = new GetUsersFilter
                {
                    Status = new Status { Inactive = true },
                    Mfa = new MFA { Disabled = true }
                }
            };
            var (_, count) = await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(query, Scope("org1"));
            count.Should().Be(1);
        }

        [Fact]
        public async Task InsertUserKeyMapAsync_Delegates()
        {
            _iam.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            (await Sut().InsertUserKeyMapAsync(new UserKeyMap { ItemId = "k1" })).Should().BeTrue();
        }

        [Fact]
        public async Task UpdateUserAsync_NormalizesAndDelegates()
        {
            User? captured = null;
            _iam.Setup(r => r.UpdateUserAsync(It.IsAny<User>()))
                .Callback<User>(u => captured = u).ReturnsAsync(true);
            (await Sut().UpdateUserAsync(new User { ItemId = "u1", Email = " A@B.COM ", UserName = " Bob " })).Should().BeTrue();
            captured!.Email.Should().Be("a@b.com");
            captured.UserName.Should().Be("bob");
        }

        [Fact]
        public async Task GetProjectIdFromProjectPeopleAsync_ReturnsTenantId()
        {
            Register(new[] { new ProjectPeople { ItemId = "pp1", UserId = "u1", TenantId = "tenant-9" } });
            (await Sut().GetProjectIdFromProjectPeopleAsync("u1")).Should().Be("tenant-9");
        }

        // ---------------------------------------------------------------------
        // User list filtering (issue #403)
        //
        // These assert the query the repository actually hands to the driver.
        // MongoMock returns every seeded item whatever the filter, so counting
        // rows would prove nothing; instead the FilterDefinition is captured on
        // its way into FindAsync, rendered, and inspected. Where the ticket
        // describes matching behaviour, the captured pattern is then run over the
        // section 7 fixture addresses. That proves the semantics of the pattern
        // the application sends -- MongoDB evaluates it with PCRE2 rather than
        // .NET, so the end-to-end walkthrough is still worth doing, but for
        // literal text and Regex.Escape output the two engines agree.
        // ---------------------------------------------------------------------

        private const string JohnDoe = "john.doe@yopmail.com";
        private const string JaneRoe = "jane.roe@yopmail.com";
        private const string JDoeSpecial = "j.doe@special.test";
        private const string JxDoe = "jXdoe@yopmail.com";

        /// <summary>
        /// Run <paramref name="query"/> through the repository and hand back the filter it built.
        /// The capturing setup supplies its own cursor: Moq prefers the newest matching setup, so
        /// without one it would shadow <see cref="MongoMock.SetupFind"/> and leave FindAsync null.
        /// </summary>
        private async Task<BsonDocument> CaptureUserFilterAsync(GetUsersFilter? filter, UserListScope? scope = null)
        {
            var col = Register(new[] { new User { ItemId = "u1" } });
            MongoMock.SetupCount(col, 1);

            FilterDefinition<User>? captured = null;
            col.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, FindOptions<User, User>, CancellationToken>(
                    (f, _, _) => captured = f)
                .ReturnsAsync(() => MongoMock.Cursor(new[] { new User { ItemId = "u1" } }));

            await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(
                new BaseGetsRequest<GetUsersFilter> { Filter = filter },
                scope ?? Scope("org-a"));

            captured.Should().NotBeNull();
            var registry = BsonSerializer.SerializerRegistry;
            return captured!.Render(new RenderArgs<User>(registry.GetSerializer<User>(), registry));
        }

        /// <summary>
        /// Find the clause for <paramref name="field"/> wherever the driver put it. Conjunctions may
        /// be flattened or kept under $and, so both shapes are searched rather than assumed.
        /// </summary>
        private static BsonValue? Clause(BsonDocument rendered, string field)
        {
            if (rendered.TryGetValue(field, out var direct)) return direct;

            foreach (var op in new[] { "$and", "$or", "$nor" })
            {
                if (!rendered.TryGetValue(op, out var branch)) continue;

                foreach (var part in branch.AsBsonArray.OfType<BsonDocument>())
                {
                    var nested = Clause(part, field);
                    if (nested is not null) return nested;
                }
            }
            return null;
        }

        /// <summary>The regex a clause carries, as a value rather than a $regex sub-document.</summary>
        private static BsonRegularExpression Regex(BsonDocument rendered, string field)
        {
            var clause = Clause(rendered, field);
            clause.Should().NotBeNull($"the filter should constrain {field}");
            return clause!.AsBsonRegularExpression;
        }

        /// <summary>Every string value anywhere in the rendered filter, regexes excluded.</summary>
        private static IEnumerable<string> AllStrings(BsonValue value) => value switch
        {
            BsonDocument doc => doc.Elements.SelectMany(e => AllStrings(e.Value)),
            BsonArray array => array.SelectMany(AllStrings),
            BsonString s => new[] { s.AsString },
            _ => Array.Empty<string>(),
        };

        /// <summary>
        /// Run the captured pattern with the options the query itself carries, not a hardcoded
        /// IgnoreCase - otherwise these checks would still pass if the repository stopped asking
        /// for a case-insensitive match.
        /// </summary>
        private static bool Matches(BsonRegularExpression regex, string candidate)
        {
            var options = System.Text.RegularExpressions.RegexOptions.None;
            if (regex.Options.Contains('i')) options |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            if (regex.Options.Contains('m')) options |= System.Text.RegularExpressions.RegexOptions.Multiline;
            if (regex.Options.Contains('s')) options |= System.Text.RegularExpressions.RegexOptions.Singleline;
            if (regex.Options.Contains('x')) options |= System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace;

            return new System.Text.RegularExpressions.Regex(regex.Pattern, options).IsMatch(candidate);
        }

        [Fact] // H1, C6
        public async Task GetUsersAsync_EmailFilter_MatchesOnSubstringCaseInsensitively()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Email = "doe" });
            var regex = Regex(rendered, "Email");

            regex.Pattern.Should().Be("doe", "plain text must not gain escapes");
            regex.Options.Should().Contain("i");
            Matches(regex, JohnDoe).Should().BeTrue();
            Matches(regex, JaneRoe).Should().BeFalse();
        }

        [Fact] // H1, example 2 -- "DOE" finds the same user as "doe"
        public async Task GetUsersAsync_EmailFilter_IsCaseInsensitiveAndTrimmed()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Email = "  DOE  " });
            var regex = Regex(rendered, "Email");

            regex.Pattern.Should().Be("doe", "the term is trimmed and lowercased before it is escaped");
            Matches(regex, JohnDoe).Should().BeTrue();
            // Matches() honours the captured options, so this only passes while the query
            // really does ask Mongo for a case-insensitive match.
            Matches(regex, JohnDoe.ToUpperInvariant()).Should().BeTrue();
        }

        [Fact] // C1, example 3 -- the dot is literal, not "any character"
        public async Task GetUsersAsync_EmailFilter_EscapesRegexSpecialCharacters()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Email = "j.doe" });
            var regex = Regex(rendered, "Email");

            regex.Pattern.Should().Be(@"j\.doe");
            Matches(regex, JDoeSpecial).Should().BeTrue();
            Matches(regex, JxDoe).Should().BeFalse("an unescaped dot would have matched the X");
        }

        [Fact] // C1
        public async Task GetUsersAsync_EmailFilter_EscapesQuantifiers()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Email = "a+b" });
            Regex(rendered, "Email").Pattern.Should().Be(@"a\+b");
        }

        // C2's "returns an empty list, not an error" is ultimately server behaviour; what is
        // provable here is that the term still yields a well-formed clause and that the pattern
        // matches none of the fixtures.
        [Fact]
        public async Task GetUsersAsync_EmailFilter_WithNoMatch_MatchesNoneOfTheFixtures()
        {
            var rendered = await CaptureUserFilterAsync(
                new GetUsersFilter { Email = "nomatch@nowhere.test" });
            var regex = Regex(rendered, "Email");

            new[] { JohnDoe, JaneRoe, JDoeSpecial }
                .Should().OnlyContain(email => !Matches(regex, email));
        }

        [Theory] // H6, C5
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetUsersAsync_BlankEmailFilter_AppliesNoEmailClause(string? email)
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Email = email });
            Clause(rendered, "Email").Should().BeNull();
        }

        [Fact] // H5 -- the same escaping gap, closed for Name
        public async Task GetUsersAsync_NameFilter_EscapesRegexSpecialCharacters()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Name = "O.Brien" });

            foreach (var field in new[] { "FirstName", "LastName" })
            {
                var regex = Regex(rendered, field);
                regex.Pattern.Should().Be(@"o\.brien");
                Matches(regex, "o'brien").Should().BeFalse();
                Matches(regex, "o.brien").Should().BeTrue();
            }
        }

        [Fact] // C6 -- plain names are untouched by the escaping fix
        public async Task GetUsersAsync_NameFilter_LeavesPlainTextUnescaped()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Name = "john" });
            Regex(rendered, "FirstName").Pattern.Should().Be("john");
        }

        [Fact]
        // The full-name clause compares a literal string, so the term it receives must stay
        // unescaped even though the regex form of the same term is escaped. Escaping both is
        // the obvious way to get this wrong, and it would silently break full-name search.
        public async Task GetUsersAsync_NameFilter_KeepsTheFullNameComparisonUnescaped()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter { Name = "o.brien" });

            // The full-name clause carries the term as a plain string; the two regex clauses
            // carry the escaped form as a regex pattern. Asserting on the string values keeps
            // this independent of how the driver serialises either one.
            var literals = AllStrings(rendered).ToList();
            literals.Should().Contain("o.brien");
            literals.Should().NotContain(@"o\.brien", "the literal Contains term must not be escaped");
            Regex(rendered, "FirstName").Pattern.Should().Be(@"o\.brien");
        }

        [Fact] // H1 -- a tenant-wide scope emits no organization clause at all
        public async Task GetUsersAsync_AllOrganizationsScope_AppliesNoOrganizationClause()
        {
            var rendered = await CaptureUserFilterAsync(
                new GetUsersFilter { Email = "doe" },
                new UserListScope(UserListScopeKind.AllOrganizations, []));

            Clause(rendered, "OrganizationIds").Should().BeNull();
            Regex(rendered, "Email").Pattern.Should().Be("doe");
        }

        [Fact] // H2 -- a single-organization scope still constrains the query
        public async Task GetUsersAsync_SingleOrganizationScope_RestrictsToThatOrganization()
        {
            var rendered = await CaptureUserFilterAsync(null, Scope("org-a"));

            Clause(rendered, "OrganizationIds").Should().NotBeNull();
            AllStrings(rendered).Should().Contain("org-a");
        }

        [Fact] // H3 -- several organizations become one union clause, not several clauses
        public async Task GetUsersAsync_MultiOrganizationScope_MatchesAnyOfThem()
        {
            var rendered = await CaptureUserFilterAsync(null, Scope("org-a", "org-b"));

            Clause(rendered, "OrganizationIds").Should().NotBeNull();
            AllStrings(rendered).Should().Contain("org-a").And.Contain("org-b");
        }

        [Fact] // C2 -- the scope decides, so an id the caller asked for never reaches the query
        public async Task GetUsersAsync_ScopeWins_OverAnythingLeftOnTheFilter()
        {
            var rendered = await CaptureUserFilterAsync(
                new GetUsersFilter { OrganizationIds = ["org-b", "org-c"] },
                Scope("org-a"));

            AllStrings(rendered).Should().Contain("org-a");
            AllStrings(rendered).Should().NotContain("org-b").And.NotContain("org-c");
        }

        [Fact] // C7 -- tenant-wide plus a null filter must not build a zero-clause conjunction
        public async Task GetUsersAsync_AllOrganizationsScope_WithNullFilter_BuildsAnEmptyFilter()
        {
            var rendered = await CaptureUserFilterAsync(
                null,
                new UserListScope(UserListScopeKind.AllOrganizations, []));

            rendered.ElementCount.Should().Be(0);
        }

        [Fact] // H6 -- the count and the page must be built from the same filter, multi-org included
        public async Task GetUsersAsync_CountsWithTheSameFilterItPages()
        {
            var col = Register(new[] { new User { ItemId = "u1" } });
            MongoMock.SetupCount(col, 1);

            FilterDefinition<User>? counted = null;
            FilterDefinition<User>? found = null;
            col.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<CountOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, CountOptions, CancellationToken>((f, _, _) => counted = f)
                .ReturnsAsync(1L);
            col.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<User>>(),
                    It.IsAny<FindOptions<User, User>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, FindOptions<User, User>, CancellationToken>((f, _, _) => found = f)
                .ReturnsAsync(() => MongoMock.Cursor(new[] { new User { ItemId = "u1" } }));

            await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(
                new BaseGetsRequest<GetUsersFilter> { Filter = new GetUsersFilter { Email = "doe" } },
                Scope("org-a", "org-b"));

            var registry = BsonSerializer.SerializerRegistry;
            var args = new RenderArgs<User>(registry.GetSerializer<User>(), registry);
            counted.Should().NotBeNull();
            found.Should().NotBeNull();
            // Compare the rendered documents, not the FilterDefinition instances: BsonDocument has
            // value equality, but FluentAssertions resolves it as a collection of elements.
            counted!.Render(args).ToJson().Should().Be(found!.Render(args).ToJson());
        }

        [Fact] // C4, H3 -- organization scope survives alongside the email filter
        public async Task GetUsersAsync_EmailFilter_StillRestrictsToTheOrganization()
        {
            var rendered = await CaptureUserFilterAsync(
                new GetUsersFilter { Email = "doe" });

            Clause(rendered, "OrganizationIds").Should().NotBeNull();
            AllStrings(rendered).Should().Contain("org-a");
            Regex(rendered, "Email").Pattern.Should().Be("doe");
        }

        [Fact] // H4 -- every other filter still lands, unchanged
        public async Task GetUsersAsync_EmailFilter_CombinesWithEveryOtherFilter()
        {
            var rendered = await CaptureUserFilterAsync(new GetUsersFilter
            {
                Email = "doe",
                Name = "john",
                Status = new Status { Active = true },
                Mfa = new MFA { Enabled = true },
                JoinedOn = DateTime.UtcNow.AddDays(-10),
                LastLogin = DateTime.UtcNow.AddDays(-1),
                UserIds = new List<string> { "u1" }
            });

            Regex(rendered, "Email").Pattern.Should().Be("doe");
            Regex(rendered, "FirstName").Pattern.Should().Be("john");

            // Assert the values too, not just that a clause exists: presence alone would still
            // pass if a neighbouring filter started emitting the wrong comparison.
            Clause(rendered, "Active")!.AsBoolean.Should().BeTrue();
            Clause(rendered, "MfaEnabled")!.AsBoolean.Should().BeTrue();
            Clause(rendered, "CreatedDate")!["$gte"].Should().NotBeNull();
            Clause(rendered, "LastLoggedInTime")!["$gte"].Should().NotBeNull();
            // ItemId is the entity's BSON id, so the driver renders the $in against _id.
            Clause(rendered, "_id")!["$in"].AsBsonArray.Select(v => v.AsString).Should().Equal("u1");
            Clause(rendered, "OrganizationIds").Should().NotBeNull();
            AllStrings(rendered).Should().Contain("org-a");
        }
    }
}

