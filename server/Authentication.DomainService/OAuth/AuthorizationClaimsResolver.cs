using Iam.DomainService.Entities;
using Iam.DomainService.Users;

namespace Authentication.DomainService.OAuth;

public sealed class ResolvedAuthorizationClaims
{
    public List<string> Roles { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
}

public interface IAuthorizationClaimsResolver
{
    Task<ResolvedAuthorizationClaims> ResolveAsync(
        User user,
        string? organizationId,
        string? requestedScope = null,
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
        string? organizationId,
        string? requestedScope = null,
        bool requireExplicitScope = false)
    {
        var roles = ResolveRoles(user, organizationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissions = ResolvePermissions(user, organizationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResolvedAuthorizationClaims
        {
            Roles = roles,
            Permissions = permissions
        };
    }

    private static IEnumerable<string> ResolveRoles(User user, string? organizationId)
    {
        List<string>? requestedRoles;
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            if (user.Roles.TryGetValue(organizationId, out requestedRoles) && requestedRoles is not null)
            {
                return requestedRoles;
            }

            return Enumerable.Empty<string>();
        }

        if (user.Roles.TryGetValue("default", out requestedRoles) && requestedRoles is not null)
        {
            return requestedRoles;
        }

        return Enumerable.Empty<string>();
    }

    private static IEnumerable<string> ResolvePermissions(User user, string? organizationId)
    {
        List<string>? requestedPermissions;
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            if (user.Permissions.TryGetValue(organizationId, out requestedPermissions) && requestedPermissions is not null)
            {
                return requestedPermissions;
            }

            return Enumerable.Empty<string>();
        }

        if (user.Permissions.TryGetValue("default", out requestedPermissions) && requestedPermissions is not null)
        {
            return requestedPermissions;
        }

        return Enumerable.Empty<string>();
    }

}