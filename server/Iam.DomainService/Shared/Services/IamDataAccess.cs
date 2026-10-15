using System.Collections.Concurrent;
using Blocks.Genesis;
using Blocks.Genesis.DataAccess;
using Blocks.Genesis.Entities.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Iam.DomainService.Services;

/// <summary>
/// Wraps <see cref="IDataAccessResolver"/> for iam. Rules (decisions.md 2026-10-15):
/// row filter + projection only (no column masking), missing schema = log and allow,
/// no user context or another tenant's DB = <see cref="ShouldSkipResolver"/>.
/// </summary>
public sealed class IamDataAccess : IIamDataAccess
{
    private readonly IDataAccessResolver _resolver;
    private readonly ILogger<IamDataAccess> _logger;
    private readonly ConcurrentDictionary<string, byte> _logged = new();

    public IamDataAccess(IDataAccessResolver resolver, ILogger<IamDataAccess> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<ReadScope<T>> ReadAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null, bool useProjection = true)
    {
        if (ShouldSkipResolver(callSite, tenantId))
            return new ReadScope<T>(true, filter, null);

        try
        {
            var resolved = await _resolver.ResolveReadAsync(schema, Render(filter), requestedFields: null);
            if (!resolved.IsAccessGranted || resolved.Filter is null)
                return new ReadScope<T>(false, filter, null);

            // useProjection=false: the caller may write the entity back (whole-doc replace), so it must be complete.
            ProjectionDefinition<T>? projection = useProjection && resolved.Projection is { ElementCount: > 0 }
                ? new BsonDocumentProjectionDefinition<T>(resolved.Projection)
                : null;
            return new ReadScope<T>(true, new BsonDocumentFilterDefinition<T>(resolved.Filter), projection);
        }
        catch (KeyNotFoundException ex)
        {
            WarnMissingSchema(schema, callSite, ex);
            return new ReadScope<T>(true, filter, null);
        }
    }

    public Task<FilterDefinition<T>?> EditFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null) =>
        MutationFilterAsync(schema, PolicyOperation.EDIT, filter, callSite, tenantId);

    public Task<FilterDefinition<T>?> DeleteFilterAsync<T>(string schema, FilterDefinition<T> filter, string callSite, string? tenantId = null) =>
        MutationFilterAsync(schema, PolicyOperation.DELETE, filter, callSite, tenantId);

    public async Task<bool> InsertAllowedAsync(string schema, string callSite, string? tenantId = null)
    {
        if (ShouldSkipResolver(callSite, tenantId))
            return true;

        try
        {
            // Empty input: this is only the WRITE gate; the entity being inserted is never restricted.
            var resolved = await _resolver.ResolveInsertAsync(schema, new Dictionary<string, object?>());
            return resolved.IsAccessGranted;
        }
        catch (KeyNotFoundException ex)
        {
            WarnMissingSchema(schema, callSite, ex);
            return true;
        }
    }

    private async Task<FilterDefinition<T>?> MutationFilterAsync<T>(string schema, PolicyOperation operation, FilterDefinition<T> filter, string callSite, string? tenantId)
    {
        if (ShouldSkipResolver(callSite, tenantId))
            return filter;

        try
        {
            var resolved = operation == PolicyOperation.DELETE
                ? await _resolver.ResolveDeleteAsync(schema, Render(filter))
                : await _resolver.ResolveMutationFilterAsync(schema, operation, Render(filter));
            return resolved.IsAccessGranted && resolved.Filter is not null
                ? new BsonDocumentFilterDefinition<T>(resolved.Filter)
                : null;
        }
        catch (KeyNotFoundException ex)
        {
            WarnMissingSchema(schema, callSite, ex);
            return filter;
        }
    }

    /// <summary>
    /// THE skip path. True when the resolver must not run, so the caller uses its original filter:
    /// (1) no authenticated user in BlocksContext (pre-login auth flows: login, token, refresh, device, signup, reset, OIDC, MFA verify; consumers; workers), or
    /// (2) the call targets another tenant's DB - the resolver loads policies from the caller's own tenant DB only.
    /// Replace this with a Genesis system principal when one exists.
    /// </summary>
    private bool ShouldSkipResolver(string callSite, string? tenantId)
    {
        var context = BlocksContext.GetContext();
        if (context is null || !context.IsAuthenticated)
        {
            LogOnce(callSite, "no-user-context");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(tenantId)
            && !string.Equals(tenantId, context.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            LogOnce(callSite, "other-tenant-db");
            return true;
        }

        return false;
    }

    private void LogOnce(string callSite, string reason)
    {
        if (_logged.TryAdd($"{callSite}|{reason}", 0))
            _logger.LogInformation("Data access resolver skipped at {CallSite}: {Reason}.", callSite, reason);
    }

    private void WarnMissingSchema(string schema, string callSite, Exception ex) =>
        _logger.LogWarning(ex, "Data access schema {Schema} not found (call site {CallSite}); allowing with the original filter.", schema, callSite);

    private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
        filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));
}
