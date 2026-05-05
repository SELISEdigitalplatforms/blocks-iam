using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;

namespace Authentication.DomainService.OAuth;

public sealed class ResolvedAuthorizationClaims
{
    public List<string> Roles { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public List<string> Resources { get; set; } = [];
}

public interface IAuthorizationClaimsResolver
{
    Task<ResolvedAuthorizationClaims> ResolveAsync(
        User user,
        string? organizationId = null,
        string? requestedScope = null,
        IEnumerable<string>? clientAllowedScopes = null,
        bool requireExplicitScope = false);
}

public sealed class AuthorizationClaimsResolver : IAuthorizationClaimsResolver
{
    private static readonly HashSet<string> ReservedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "openid",
        "profile",
        "email",
        "offline_access",
        "offline",
        "address",
        "phone"
    };

    private readonly IUserRepository _userRepository;

    public AuthorizationClaimsResolver(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ResolvedAuthorizationClaims> ResolveAsync(
        User user,
        string? organizationId = null,
        string? requestedScope = null,
        IEnumerable<string>? clientAllowedScopes = null,
        bool requireExplicitScope = false)
    {
        var roles = ResolveRoles(user, organizationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var directPermissionKeys = ResolvePermissions(user, organizationId)
            .Where(IsNamespacedPermission)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissionCatalog = new List<GetUserPermission>();

        if (directPermissionKeys.Count > 0)
        {
            permissionCatalog.AddRange(await _userRepository.GetPermissionsByResourcesAsync(directPermissionKeys));
        }

        var resolvedPermissions = permissionCatalog
            .Where(permission => IsNamespacedPermission(permission.Resource))
            .GroupBy(permission => permission.Resource, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var allowedClientScopes = clientAllowedScopes?
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filteredPermissions = ApplyScopeFilter(
            resolvedPermissions,
            requestedScope,
            allowedClientScopes,
            requireExplicitScope);

        var resources = filteredPermissions
            .Select(permission => ExtractResourceNamespace(permission.Resource))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResolvedAuthorizationClaims
        {
            Roles = roles,
            Permissions = filteredPermissions
                .Select(permission => permission.Resource)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Resources = resources
        };
    }

    private static IEnumerable<string> ResolveRoles(User user, string? organizationId)
    {
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            if (user.Roles.TryGetValue(organizationId, out var requestedRoles) && requestedRoles is not null)
            {
                return requestedRoles;
            }

            return [];
        }

        if (user.Roles.TryGetValue("default", out var defaultRoles) && defaultRoles is not null)
        {
            return defaultRoles;
        }

        return user.Roles.Values.FirstOrDefault() ?? [];
    }

    private static IEnumerable<string> ResolvePermissions(User user, string? organizationId)
    {
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            if (user.Permissions.TryGetValue(organizationId, out var requestedPermissions) && requestedPermissions is not null)
            {
                return requestedPermissions;
            }

            return [];
        }

        if (user.Permissions.TryGetValue("default", out var defaultPermissions) && defaultPermissions is not null)
        {
            return defaultPermissions;
        }

        return user.Permissions.Values.FirstOrDefault() ?? [];
    }

    private static List<GetUserPermission> ApplyScopeFilter(
        IEnumerable<GetUserPermission> permissions,
        string? requestedScope,
        IReadOnlyCollection<string>? clientAllowedScopes,
        bool requireExplicitScope)
    {
        var filtered = permissions.ToList();

        if (clientAllowedScopes is { Count: > 0 })
        {
            filtered = filtered
                .Where(permission => clientAllowedScopes.Any(scope => ScopeMatchesPermission(scope, permission.Resource)))
                .ToList();
        }

        var requestedCustomScopes = ParseCustomScopes(requestedScope);
        if (requestedCustomScopes.Count == 0)
        {
            return requireExplicitScope ? [] : filtered;
        }

        return filtered
            .Where(permission => requestedCustomScopes.Any(scope => ScopeMatchesPermission(scope, permission.Resource)))
            .ToList();
    }

    private static HashSet<string> ParseCustomScopes(string? scope)
    {
        return scope?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !ReservedScopes.Contains(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    private static bool ScopeMatchesPermission(string scope, string permission)
    {
        if (string.Equals(scope, permission, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var resourceNamespace = ExtractResourceNamespace(permission);
        if (string.Equals(scope, resourceNamespace, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return permission.StartsWith(scope + ".", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith(scope + ":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamespacedPermission(string? permission)
    {
        return !string.IsNullOrWhiteSpace(permission)
            && (permission.Contains(':', StringComparison.Ordinal) || permission.Contains('.', StringComparison.Ordinal));
    }

    private static string ExtractResourceNamespace(string permission)
    {
        var separatorIndex = permission.IndexOfAny([':', '.']);
        return separatorIndex > 0 ? permission[..separatorIndex] : permission;
    }
}