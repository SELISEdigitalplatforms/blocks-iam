using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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
public sealed record ReadScope<T>(bool Granted, FilterDefinition<T> Filter, ProjectionDefinition<T>? Projection,
    Func<Dictionary<string, object>, Dictionary<string, object>>? RowMask = null)
{
    public IFindFluent<T, T> Apply(IMongoCollection<T> collection)
    {
        var find = collection.Find(Filter);
        return Projection is null ? find : find.Project<T>(Projection);
    }

    /// <summary>
    /// For what goes back to the API caller: properties a View column rule hides from them are cleared (a
    /// Genesis column mask, as the Data Gateway applies). Never save a masked entity back: write paths read the
    /// whole document instead. No-op on the skip path and where no column rule applies.
    /// </summary>
    public T ForView(T entity) => RowMask is null || entity is null ? entity : ColumnMask.Apply(entity, RowMask);

    /// <inheritdoc cref="ForView(T)"/>
    public List<T> ForView(List<T> entities) => RowMask is null ? entities : entities.ConvertAll(ForView);

    /// <summary>
    /// <see cref="ForView(T)"/> for a projection of the entity (e.g. User read as GetAccounts): the stored
    /// property names, so the same column rules apply.
    /// </summary>
    public TOut ForViewAs<TOut>(TOut item) => RowMask is null || item is null ? item : ColumnMask.Apply(item, RowMask);

    /// <inheritdoc cref="ForViewAs{TOut}(TOut)"/>
    public List<TOut> ForViewAs<TOut>(List<TOut> items) => RowMask is null ? items : items.ConvertAll(ForViewAs);
}

/// <summary>
/// The one door between iam and the Genesis data access resolver (row filter + projection; column
/// masking only on reads that opt in with ReadScope.ForView).
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

    /// <summary>
    /// Default values and validation rules for an insert or a whole-document replace. On insert, defaults
    /// fill the entity's null properties, in the instance passed in (which is also returned). Schemas the
    /// Genesis allow-list keeps closed skip them. Throws <see cref="Blocks.Genesis.DataAccess.Rules.DataValidationException"/> when a rule fails.
    /// On insert the requester's user id is then added to the record's Read, Update and Delete UserIds.
    /// </summary>
    Task<T> PrepareWriteAsync<T>(string schema, T entity, bool isInsert, string callSite, string? tenantId = null);

    /// <summary>
    /// As <see cref="PrepareWriteAsync{T}"/> for an upsert: it is an insert when nothing matches
    /// <paramref name="filter"/> in <paramref name="collection"/>.
    /// </summary>
    Task<T> PrepareUpsertAsync<T>(string schema, T entity, IMongoCollection<T> collection, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>
    /// For a whole-document replace of what <paramref name="filter"/> matches: as <see cref="PrepareWriteAsync{T}"/>
    /// (not an insert), after putting back the stored values of properties a View column rule hides from the caller
    /// when the replacement carries what their masked read showed (PrepareUpsertAsync does the same on update).
    /// </summary>
    Task<T> PrepareReplaceAsync<T>(string schema, T entity, IMongoCollection<T> collection, FilterDefinition<T> filter, string callSite, string? tenantId = null);

    /// <summary>
    /// Validation rules for a partial update: only the <c>$set</c> fields are checked, the same "only what is
    /// sent" rule as the Data Gateway; defaults never apply. Throws
    /// <see cref="Blocks.Genesis.DataAccess.Rules.DataValidationException"/> when a rule fails.
    /// </summary>
    Task CheckUpdateAsync<T>(string schema, UpdateDefinition<T> update, string callSite, string? tenantId = null);
}

/// <summary>Applies a Genesis column mask (built for rows) to a typed entity.</summary>
internal static class ColumnMask
{
    public static T Apply<T>(T entity, Func<Dictionary<string, object>, Dictionary<string, object>> mask)
    {
        var document = entity.ToBsonDocument();
        var row = mask(document.ToDictionary());
        return RemoveHidden(document, row) ? BsonSerializer.Deserialize<T>(document) : entity;
    }

    /// <summary>
    /// For a whole-document update: puts back the stored value of every property the caller cannot view (a View
    /// column rule hides it) when the update carries exactly what the masked read showed them, so a client that
    /// saves what it read never erases it. A different value the caller sent is kept.
    /// </summary>
    public static T KeepHidden<T>(T incoming, T stored, Func<Dictionary<string, object>, Dictionary<string, object>> mask)
    {
        var storedDocument = stored.ToBsonDocument();
        var view = storedDocument.DeepClone().AsBsonDocument;
        if (!RemoveHidden(view, mask(storedDocument.ToDictionary())))
            return incoming;
        var shown = BsonSerializer.Deserialize<T>(view).ToBsonDocument();
        var update = incoming.ToBsonDocument();
        return Restore(update, storedDocument, view, shown) ? BsonSerializer.Deserialize<T>(update) : incoming;
    }

    /// <summary>Puts back, property by property, what the mask took out of <paramref name="view"/>. True when anything was.</summary>
    private static bool Restore(BsonDocument update, BsonDocument stored, BsonDocument view, BsonDocument shown)
    {
        var changed = false;
        foreach (var element in stored)
        {
            if (element.Value.IsBsonNull)
                continue;
            if (!view.TryGetValue(element.Name, out var viewValue))
            {
                var sent = update.GetValue(element.Name, BsonNull.Value);
                if (sent.IsBsonNull || sent.Equals(shown.GetValue(element.Name, BsonNull.Value)))
                {
                    update[element.Name] = element.Value;
                    changed = true;
                }
            }
            else if (element.Value is BsonDocument nested && viewValue is BsonDocument nestedView
                     && update.GetValue(element.Name, BsonNull.Value) is BsonDocument nestedUpdate
                     && shown.GetValue(element.Name, BsonNull.Value) is BsonDocument nestedShown)
            {
                changed |= Restore(nestedUpdate, nested, nestedView, nestedShown);
            }
        }
        return changed;
    }

    /// <summary>
    /// Takes out of <paramref name="document"/> what the mask cleared in <paramref name="row"/> (set to null or
    /// removed), so a hidden property reads as unset; every other value stays exactly as stored. True when anything was.
    /// </summary>
    private static bool RemoveHidden(BsonDocument document, IDictionary<string, object> row)
    {
        var changed = false;
        foreach (var element in document.ToList())
        {
            if (element.Value.IsBsonNull)
                continue;
            if (!row.TryGetValue(element.Name, out var masked) || masked is null)
            {
                document.Remove(element.Name);
                changed = true;
            }
            else if (element.Value is BsonDocument nested && masked is IDictionary<string, object> nestedRow)
            {
                changed |= RemoveHidden(nested, nestedRow);
            }
            else if (element.Value is BsonArray array && masked is System.Collections.IList items && items.Count == array.Count)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is BsonDocument item && items[i] is IDictionary<string, object> itemRow)
                        changed |= RemoveHidden(item, itemRow);
                }
            }
        }
        return changed;
    }
}
