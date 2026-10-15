using MongoDB.Driver;

namespace Iam.DomainService.Services;

/// <summary>Schema names of the Genesis system entities that iam owns (= entity class name).</summary>
public static class IamSchemas
{
    public const string User = "User";
    public const string Role = "Role";
    public const string Permission = "Permission";
    public const string Organization = "Organization";
    public const string UserKeyMap = "UserKeyMap";
    public const string IdentityConfiguration = "IdentityConfiguration";
    public const string IdentityProvider = "IdentityProvider";
    public const string ClientCredential = "ClientCredential";
    public const string OidcClientRegistration = "OidcClientRegistration";
    public const string RefreshTokenModel = "RefreshTokenModel";
    public const string IdpSessionModel = "IdpSessionModel";
    public const string TokenRevocationModel = "TokenRevocationModel";
    public const string DeviceAuthorizationRequestModel = "DeviceAuthorizationRequestModel";
    public const string SignupLink = "SignupLink";
    public const string SignupLinkConfiguration = "SignupLinkConfiguration";
    public const string UserActivity = "UserActivity";
    public const string UserTotpDetail = "UserTotpDetail";
    public const string ImpersonationSession = "ImpersonationSession";
    public const string BiometricCredential = "BiometricCredential";
    public const string BlocksClientConfig = "BlocksClientConfig";

    private static readonly HashSet<string> Governed =
    [
        User, Role, Permission, Organization, UserKeyMap, IdentityConfiguration, IdentityProvider, ClientCredential,
        OidcClientRegistration, RefreshTokenModel, IdpSessionModel, TokenRevocationModel, DeviceAuthorizationRequestModel,
        SignupLink, SignupLinkConfiguration, UserActivity, UserTotpDetail, ImpersonationSession, BiometricCredential, BlocksClientConfig
    ];

    /// <summary>
    /// Schema for a generic repository call by entity type. Local shadow classes of User (<c>UserInfo</c>,
    /// <c>UserMfaInfo</c>) map to User. Null = not a governed entity (call stays untouched).
    /// </summary>
    public static string? For(Type entityType) => entityType.Name switch
    {
        "UserInfo" or "UserMfaInfo" => User,
        var name when Governed.Contains(name) => name,
        _ => null
    };
}

/// <summary>
/// A resolved read. <see cref="Granted"/> false means "treat as not found / empty".
/// <see cref="Apply"/> builds the Find with the resolver's filter and projection.
/// </summary>
public sealed record ReadScope<T>(bool Granted, FilterDefinition<T> Filter, ProjectionDefinition<T>? Projection)
{
    public IFindFluent<T, T> Apply(IMongoCollection<T> collection)
    {
        var find = collection.Find(Filter);
        return Projection is null ? find : find.Project<T>(Projection);
    }
}

/// <summary>
/// The one door between iam and the Genesis data access resolver (row filter + projection only).
/// Every call on a governed iam collection goes through it. Pass the tenant DB the call
/// targets in <c>tenantId</c> (null = the caller's own DB).
/// </summary>
public interface IIamDataAccess
{
    /// <summary>Row filter and projection for a read. Denied: <see cref="ReadScope{T}.Granted"/> is false.</summary>
    Task<ReadScope<T>> ReadAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null, bool useProjection = true);

    /// <summary>Filter for an update or replace (EDIT). Null = denied, do not write.</summary>
    Task<FilterDefinition<T>?> EditFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>Filter for a delete (DELETE). Null = denied, do not write.</summary>
    Task<FilterDefinition<T>?> DeleteFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>WRITE gate for an insert. Input fields are never dropped. False = denied.</summary>
    Task<bool> InsertAllowedAsync(string schema, string callSite, string? tenantId = null);
}
