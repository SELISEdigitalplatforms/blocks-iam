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

    /// <summary>
    /// One unordered bulk write of inserts and updates, each gated like its single-document helper:
    /// inserts by the WRITE gate and the schema's defaults and rules, updates by the EDIT filter and the
    /// <c>$set</c> rules. An operation that is denied, breaks a rule or fails on the server fails alone;
    /// the others are still written. Indexes in the result count inserts first, then updates.
    /// </summary>
    public static async Task<BulkWriteOutcome> BulkWriteAsync<T>(this IIamDataAccess access, IMongoCollection<T> collection, string schema,
        IReadOnlyList<T> inserts, IReadOnlyList<(FilterDefinition<T> Filter, UpdateDefinition<T> Update)> updates, string callSite)
    {
        var failed = new HashSet<int>();
        var duplicates = new HashSet<int>();
        var models = new List<WriteModel<T>>();
        var modelIndexes = new List<int>();

        await AddInsertModelsAsync(access, schema, inserts, callSite, models, modelIndexes, failed);
        for (var i = 0; i < updates.Count; i++)
        {
            var index = inserts.Count + i;
            var model = await GovernedUpdateModelAsync(access, schema, updates[i].Filter, updates[i].Update, callSite);
            if (model is null)
            {
                failed.Add(index);
                continue;
            }
            models.Add(model);
            modelIndexes.Add(index);
        }

        if (models.Count == 0)
            return new BulkWriteOutcome(failed, duplicates);

        try
        {
            await collection.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false });
        }
        catch (MongoBulkWriteException<T> ex)
        {
            foreach (var error in ex.WriteErrors)
            {
                var index = modelIndexes[error.Index];
                (error.Category == ServerErrorCategory.DuplicateKey ? duplicates : failed).Add(index);
            }
        }

        return new BulkWriteOutcome(failed, duplicates);
    }

    private static async Task AddInsertModelsAsync<T>(IIamDataAccess access, string schema, IReadOnlyList<T> inserts, string callSite,
        List<WriteModel<T>> models, List<int> modelIndexes, HashSet<int> failed)
    {
        if (inserts.Count == 0)
            return;

        if (!await access.InsertAllowedAsync(schema, callSite))
        {
            for (var i = 0; i < inserts.Count; i++)
                failed.Add(i);
            return;
        }

        for (var i = 0; i < inserts.Count; i++)
        {
            try
            {
                await access.PrepareWriteAsync(schema, inserts[i], isInsert: true, callSite);
                models.Add(new InsertOneModel<T>(inserts[i]));
                modelIndexes.Add(i);
            }
            catch (Blocks.Genesis.DataAccess.Rules.DataValidationException)
            {
                failed.Add(i);
            }
        }
    }

    /// <summary>The update as a bulk model with the EDIT filter applied; null when it is denied or breaks a rule.</summary>
    private static async Task<WriteModel<T>?> GovernedUpdateModelAsync<T>(IIamDataAccess access, string schema,
        FilterDefinition<T> filter, UpdateDefinition<T> update, string callSite)
    {
        var edit = await access.EditFilterAsync(schema, filter, callSite);
        if (edit is null)
            return null;
        try
        {
            await access.CheckUpdateAsync(schema, update, callSite);
        }
        catch (Blocks.Genesis.DataAccess.Rules.DataValidationException)
        {
            return null;
        }
        return new UpdateOneModel<T>(edit, update);
    }

    public static async Task EnsureInsertAllowedAsync(this IIamDataAccess access, string schema, string callSite, string? tenantId = null)
    {
        if (!await access.InsertAllowedAsync(schema, callSite, tenantId))
            throw new UnauthorizedAccessException($"Data access policy denied writing {schema} ({callSite}).");
    }
}

/// <summary>
/// What a governed bulk write could not write, by operation index (inserts first, then updates).
/// <see cref="DuplicateKeys"/> are refused by a unique index; <see cref="Failed"/> covers every other reason.
/// </summary>
public sealed record BulkWriteOutcome(IReadOnlySet<int> Failed, IReadOnlySet<int> DuplicateKeys);
