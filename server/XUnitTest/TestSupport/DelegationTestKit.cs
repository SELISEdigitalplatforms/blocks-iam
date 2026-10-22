using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.DelegatedAdministration;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace XUnitTest.TestSupport
{
    /// <summary>
    /// A real <see cref="DelegatedAdministrationService"/> over an in-memory organization, for tests
    /// of the rules and of every call site. Enforce by default, so a would-deny is visible as a refusal.
    /// </summary>
    internal sealed class DelegationTestKit
    {
        public const string Org = "org-acme-1234";
        public const string Tenant = "tenant-1";

        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Role>> _roles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Permission>> _permissions = new(StringComparer.Ordinal);

        public DelegationTestKit(DelegatedAdministrationMode mode = DelegatedAdministrationMode.Enforce)
        {
            Options = new TestOptionsMonitor(new DelegatedAdministrationOptions { Mode = mode });
            Repository.Setup(r => r.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => _users.GetValueOrDefault(id));
            Repository.Setup(r => r.GetRolesAsync(It.IsAny<string>()))
                .ReturnsAsync((string org) => _roles.GetValueOrDefault(org)?.ToList() ?? []);
            Repository.Setup(r => r.GetPermissionsAsync(It.IsAny<string>()))
                .ReturnsAsync((string org) => _permissions.GetValueOrDefault(org)?.ToList() ?? []);
            Repository.Setup(r => r.GetPolicyRoleSlugsAsync()).ReturnsAsync(PolicyRoles);
            Repository.Setup(r => r.GetUserIdsWithRoleAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string slug, string org) => _users.Values
                    .Where(u => u.Roles.TryGetValue(org, out var held) && held.Contains(slug))
                    .Select(u => u.ItemId)
                    .ToList());
            LegacyResources.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((new List<Role>().AsQueryable(), 0L));
            Terminator.Setup(t => t.EndAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        }

        public Mock<IDelegatedAdministrationRepository> Repository { get; } = new();

        public Mock<IUserSessionTerminator> Terminator { get; } = new();

        /// <summary>What the system user's unchanged, pre-existing grant check reads.</summary>
        public Mock<IResourceRepository> LegacyResources { get; } = new();

        public Mock<IIdentityAccessManagementService> Iam { get; } = new();

        public Mock<ITenants> Tenants { get; } = new();

        public TestOptionsMonitor Options { get; }

        public ListLogger Logger { get; } = new();

        public HashSet<string> PolicyRoles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DelegatedAdministrationService Service() =>
            new(Repository.Object, Options, Tenants.Object, Iam.Object, Logger,
                new GrantAuthorizationService(LegacyResources.Object), Terminator.Object);

        public DelegationTestKit WithRole(string slug, string? parent = null, string organizationId = Org)
        {
            var roles = RolesOf(organizationId);
            var ancestors = new List<string>();
            var cursor = parent;
            while (!string.IsNullOrWhiteSpace(cursor))
            {
                ancestors.Add(cursor);
                cursor = roles.FirstOrDefault(r => r.Slug == cursor)?.ParentRoleSlug;
            }

            roles.Add(new Role
            {
                ItemId = organizationId == Org ? "role-" + slug : $"role-{organizationId}-{slug}",
                Slug = slug,
                Name = slug,
                OrganizationId = organizationId,
                ParentRoleSlug = parent,
                AncestorRoleSlugs = ancestors,
                CanCreateOwn = false
            });
            return this;
        }

        public DelegationTestKit WithPermission(string resource, string organizationId = Org, params string[] roles)
        {
            PermissionsOf(organizationId).Add(new Permission
            {
                ItemId = organizationId == Org ? "perm-" + resource : $"perm-{organizationId}-{resource}",
                Resource = resource,
                Name = resource,
                OrganizationId = organizationId,
                Roles = roles.ToList()
            });
            return this;
        }

        public User WithUser(
            string id,
            IEnumerable<string>? roles = null,
            IEnumerable<string>? permissions = null,
            string organizationId = Org,
            Dictionary<string, object>? attributes = null)
        {
            var user = new User
            {
                ItemId = id,
                Email = id + "@example.com",
                OrganizationIds = [organizationId],
                Roles = new Dictionary<string, List<string>> { [organizationId] = (roles ?? []).ToList() },
                Permissions = new Dictionary<string, List<string>> { [organizationId] = (permissions ?? []).ToList() },
                Attributes = attributes ?? new Dictionary<string, object>()
            };
            _users[id] = user;
            return user;
        }

        public User? FindUser(string id) => _users.GetValueOrDefault(id);

        public IList<Role> RolesOf(string organizationId = Org)
        {
            if (!_roles.TryGetValue(organizationId, out var roles))
            {
                roles = [];
                _roles[organizationId] = roles;
            }

            return roles;
        }

        public IList<Permission> PermissionsOf(string organizationId = Org)
        {
            if (!_permissions.TryGetValue(organizationId, out var permissions))
            {
                permissions = [];
                _permissions[organizationId] = permissions;
            }

            return permissions;
        }

        /// <summary>
        /// The usual tree: admin > manager > viewer, plus standalone "auditor" and "owner".
        /// admin carries read/write/delete, manager read/write, viewer read, auditor read,
        /// owner read/write/delete/billing.
        /// </summary>
        public DelegationTestKit WithStandardTree(string organizationId = Org)
        {
            WithRole("admin", organizationId: organizationId)
                .WithRole("manager", "admin", organizationId)
                .WithRole("viewer", "manager", organizationId)
                .WithRole("auditor", organizationId: organizationId)
                .WithRole("owner", organizationId: organizationId);
            WithPermission("read", organizationId, "admin", "manager", "viewer", "auditor", "owner")
                .WithPermission("write", organizationId, "admin", "manager", "owner")
                .WithPermission("delete", organizationId, "admin", "owner")
                .WithPermission("billing", organizationId, "owner");
            return this;
        }

        /// <summary>Installs a signed-in caller as the current BlocksContext (test mode).</summary>
        public static void SignIn(string userId, string? organizationId = Org, bool impersonated = false, bool authenticated = true)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: Tenant, roles: null, userId: userId, impersonated: impersonated,
                isAuthenticated: authenticated, requestUri: "https://test", organizationId: organizationId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: Tenant, impersonationSessionId: string.Empty, applicationDomain: "test"));
        }

        public static void SignOut()
        {
            BlocksContext.SetContext(null!);
            BlocksContext.IsTestMode = false;
        }

        public bool Logged(string fragment) => Logger.Messages.Any(m => m.Contains(fragment, StringComparison.Ordinal));
    }

    internal sealed class TestOptionsMonitor : IOptionsMonitor<DelegatedAdministrationOptions>
    {
        public TestOptionsMonitor(DelegatedAdministrationOptions value) => CurrentValue = value;

        public DelegatedAdministrationOptions CurrentValue { get; set; }

        public DelegatedAdministrationOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<DelegatedAdministrationOptions, string?> listener) => null;
    }

    internal sealed class ListLogger : ILogger<DelegatedAdministrationService>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            _messages.Add(formatter(state, exception));
        }
    }
}
