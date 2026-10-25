using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.TestSupport
{
    /// <summary>
    /// A real <see cref="AccessGrantService"/> over an in-memory organization, for tests
    /// of the rules and of every call site. A would-deny is visible as a refusal.
    /// </summary>
    internal sealed class AccessGrantTestKit
    {
        public const string Org = "org-acme-1234";
        public const string Tenant = "tenant-1";

        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Role>> _roles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Permission>> _permissions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClientCredential> _clients = new(StringComparer.Ordinal);

        public AccessGrantTestKit()
        {
            Repository.Setup(r => r.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => _users.GetValueOrDefault(id));
            Repository.Setup(r => r.GetClientCredentialAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => _clients.GetValueOrDefault(id));
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

        public Mock<IAccessGrantRepository> Repository { get; } = new();

        public Mock<IUserSessionTerminator> Terminator { get; } = new();

        /// <summary>What the system user's unchanged, pre-existing grant check reads.</summary>
        public Mock<IResourceRepository> LegacyResources { get; } = new();

        public Mock<IIdentityAccessManagementService> Iam { get; } = new();

        public Mock<ITenants> Tenants { get; } = new();

        public ListLogger Logger { get; } = new();

        public HashSet<string> PolicyRoles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public AccessGrantService Service() =>
            new(Repository.Object, Tenants.Object, Iam.Object, Logger,
                new GrantAuthorizationService(LegacyResources.Object), Terminator.Object);

        public AccessGrantTestKit WithRole(string slug, string? parent = null, string organizationId = Org)
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

        public AccessGrantTestKit WithPermission(string resource, string organizationId = Org, params string[] roles)
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

        public ClientCredential WithClient(
            string id,
            IEnumerable<string>? roles = null,
            IEnumerable<string>? permissions = null,
            string organizationId = Org,
            bool isActive = true)
        {
            var client = new ClientCredential
            {
                ItemId = id,
                Name = id,
                OrganizationId = organizationId,
                Roles = (roles ?? []).ToList(),
                Permissions = (permissions ?? []).ToList(),
                IsActive = isActive
            };
            _clients[id] = client;
            return client;
        }

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
        /// owner read/write/delete/billing. admin and manager also carry the two manage permissions,
        /// so an admin holder is a top user and a manager holder a descendant.
        /// </summary>
        public AccessGrantTestKit WithStandardTree(string organizationId = Org)
        {
            WithRole("admin", organizationId: organizationId)
                .WithRole("manager", "admin", organizationId)
                .WithRole("viewer", "manager", organizationId)
                .WithRole("auditor", organizationId: organizationId)
                .WithRole("owner", organizationId: organizationId);
            WithPermission("read", organizationId, "admin", "manager", "viewer", "auditor", "owner")
                .WithPermission("write", organizationId, "admin", "manager", "owner")
                .WithPermission("delete", organizationId, "admin", "owner")
                .WithPermission("billing", organizationId, "owner")
                .WithPermission(AccessGrantRules.MutateRolesPermission, organizationId, "admin", "manager")
                .WithPermission(AccessGrantRules.MutateUsersPermission, organizationId, "admin", "manager");
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

        /// <summary>Installs a client-credentials caller (a client id, no user) as the current BlocksContext.</summary>
        public static void SignInClient(string clientId, string? organizationId = Org, bool authenticated = true)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: Tenant, roles: null, userId: null, isAuthenticated: authenticated,
                requestUri: "https://test", organizationId: organizationId, expireOn: DateTime.UtcNow.AddHours(1),
                email: null, permissions: null, userName: null, phoneNumber: null, displayName: null,
                oauthToken: null, originalTenantId: Tenant, applicationDomain: "test", clientId: clientId));
        }

        public static void SignOut()
        {
            BlocksContext.SetContext(null!);
            BlocksContext.IsTestMode = false;
        }

        public bool Logged(string fragment) => Logger.Messages.Any(m => m.Contains(fragment, StringComparison.Ordinal));
    }

    internal sealed class ListLogger : ILogger<AccessGrantService>
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
