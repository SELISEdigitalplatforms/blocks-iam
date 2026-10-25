using MongoDB.Driver;

namespace Iam.DomainService.Services;

/// <summary>
/// Thin helpers over <see cref="IIamDataAccess"/> so each call site stays one line. Denied reads answer
/// "not found / empty / 0"; denied updates and deletes do not write (null result); a denied insert throws
/// <see cref="UnauthorizedAccessException"/> because dropping it silently would return an unsaved entity.
/// Upserts are gated by the EDIT check but keep their own filter (an AND-ed row filter on an upsert would insert a duplicate).
/// Writes then get the schema's validation rules, and inserts its default values (<see cref="IIamDataAccess.PrepareWriteAsync{T}"/>);
/// a failed rule throws <see cref="Blocks.Genesis.DataAccess.Rules.DataValidationException"/> and nothing is written.
/// </summary>
public static class IamDataAccessExtensions
{
    public static async Task<List<T>> FindListAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, bool useProjection = false, string? tenantId = null)
    {
        var scope = await access.ReadAsync(schema, filter, callSite, tenantId, useProjection);
        return scope.Granted ? await scope.Apply(collection).ToListAsync() : [];
    }

    public static async Task<T?> FindFirstAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, bool useProjection = false, string? tenantId = null)
    {
        var scope = await access.ReadAsync(schema, filter, callSite, tenantId, useProjection);
        return scope.Granted ? await scope.Apply(collection).FirstOrDefaultAsync() : default;
    }

    /// <summary>As <see cref="FindListAsync{T}"/>, for results that go back to the API caller: View column (property) rules clear what they may not see. Never save these back.</summary>
    public static async Task<List<T>> FindListForViewAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, string? tenantId = null)
    {
        var scope = await access.ReadAsync(schema, filter, callSite, tenantId);
        return scope.Granted ? scope.ForView(await scope.Apply(collection).ToListAsync()) : [];
    }

    /// <summary>As <see cref="FindFirstAsync{T}"/>, for a result that goes back to the API caller: View column (property) rules clear what they may not see. Never save it back.</summary>
    public static async Task<T?> FindFirstForViewAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, string? tenantId = null)
    {
        var scope = await access.ReadAsync(schema, filter, callSite, tenantId);
        return scope.Granted ? scope.ForView(await scope.Apply(collection).FirstOrDefaultAsync()) : default;
    }

    public static async Task<long> CountAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, string? tenantId = null)
    {
        var scope = await access.ReadAsync(schema, filter, callSite, tenantId, useProjection: false);
        return scope.Granted ? await collection.CountDocumentsAsync(scope.Filter) : 0;
    }

    public static async Task<bool> ExistsAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, string? tenantId = null) =>
        await access.CountAsync(collection, schema, filter, callSite, tenantId) > 0;

    public static async Task<ReplaceOneResult?> ReplaceOneAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, T replacement, string callSite, ReplaceOptions? options = null)
    {
        var edit = await access.EditFilterAsync(schema, filter, callSite);
        if (edit is null) return null;
        replacement = options?.IsUpsert == true
            ? await access.PrepareUpsertAsync(schema, replacement, collection, filter, callSite)
            : await access.PrepareWriteAsync(schema, replacement, isInsert: false, callSite);
        return await collection.ReplaceOneAsync(options?.IsUpsert == true ? filter : edit, replacement, options ?? new ReplaceOptions());
    }

    public static async Task<UpdateResult?> UpdateOneAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, UpdateDefinition<T> update, string callSite, UpdateOptions? options = null)
    {
        var edit = await access.EditFilterAsync(schema, filter, callSite);
        if (edit is null) return null;
        await access.CheckUpdateAsync(schema, update, callSite);
        return await collection.UpdateOneAsync(options?.IsUpsert == true ? filter : edit, update, options);
    }

    public static async Task<UpdateResult?> UpdateManyAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, UpdateDefinition<T> update, string callSite, string? tenantId = null)
    {
        var edit = await access.EditFilterAsync(schema, filter, callSite, tenantId);
        if (edit is null) return null;
        await access.CheckUpdateAsync(schema, update, callSite, tenantId);
        return await collection.UpdateManyAsync(edit, update);
    }

    public static async Task<T?> FindOneAndUpdateAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, UpdateDefinition<T> update, string callSite, FindOneAndUpdateOptions<T>? options = null)
    {
        var edit = await access.EditFilterAsync(schema, filter, callSite);
        if (edit is null) return default;
        await access.CheckUpdateAsync(schema, update, callSite);
        return await collection.FindOneAndUpdateAsync(edit, update, options);
    }

    public static async Task<DeleteResult?> DeleteOneAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, string? tenantId = null)
    {
        var del = await access.DeleteFilterAsync(schema, filter, callSite, tenantId);
        return del is null ? null : await collection.DeleteOneAsync(del);
    }

    public static async Task<DeleteResult?> DeleteManyAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        FilterDefinition<T> filter, string callSite, string? tenantId = null)
    {
        var del = await access.DeleteFilterAsync(schema, filter, callSite, tenantId);
        return del is null ? null : await collection.DeleteManyAsync(del);
    }

    public static async Task InsertOneAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        T document, string callSite, string? tenantId = null)
    {
        await access.EnsureInsertAllowedAsync(schema, callSite, tenantId);
        await access.PrepareWriteAsync(schema, document, isInsert: true, callSite, tenantId);
        await collection.InsertOneAsync(document);
    }

    public static async Task InsertManyAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        IEnumerable<T> documents, string callSite, string? tenantId = null)
    {
        await access.EnsureInsertAllowedAsync(schema, callSite, tenantId);
        var list = documents as IList<T> ?? documents.ToList();
        foreach (var document in list)
            await access.PrepareWriteAsync(schema, document, isInsert: true, callSite, tenantId);
        await collection.InsertManyAsync(list);
    }

    public static async Task EnsureInsertAllowedAsync(this IIamDataAccess access, string schema, string callSite, string? tenantId = null)
    {
        if (!await access.InsertAllowedAsync(schema, callSite, tenantId))
            throw new UnauthorizedAccessException($"Data access policy denied writing {schema} ({callSite}).");
    }
}
