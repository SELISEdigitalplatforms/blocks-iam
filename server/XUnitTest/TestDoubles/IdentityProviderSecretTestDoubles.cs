using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Authentication.DomainService.OAuth;
using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.TestDoubles;

/// <summary>Shared doubles for identity-provider secret handling (blocks-iam#649).</summary>
internal static class IdentityProviderSecretTestDoubles
{
    /// <summary>
    /// A real <see cref="IdentityProviderSecretResolver"/> whose DI scopes serve
    /// <paramref name="secrets"/> as the scoped <see cref="ISecretService"/>. The mock doubles as the
    /// counting fake: verify calls on it. Without one, a strict mock fails any secret call, which
    /// suits callers that only use legacy (plaintext) providers and supply no secret.
    /// </summary>
    public static IdentityProviderSecretResolver Resolver(
        Mock<ISecretService>? secrets = null,
        ILogger<IdentityProviderSecretResolver>? logger = null)
    {
        var secretService = (secrets ?? new Mock<ISecretService>(MockBehavior.Strict)).Object;
        var services = new ServiceCollection();
        services.AddScoped(_ => secretService);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return new IdentityProviderSecretResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<IdentityProviderSecretResolver>.Instance);
    }

    /// <summary>An unauthenticated ambient context for <paramref name="tenantId"/>, as on an anonymous sign-in callback.</summary>
    public static BlocksContext AnonymousContext(string tenantId) =>
        BlocksContext.Create(
            tenantId: tenantId, roles: [], userId: string.Empty, isAuthenticated: false,
            requestUri: "/api/social/callback", organizationId: "org-1", expireOn: DateTime.UtcNow.AddMinutes(5),
            email: string.Empty, permissions: [], userName: string.Empty, phoneNumber: string.Empty,
            displayName: string.Empty, oauthToken: string.Empty, originalTenantId: tenantId);

    /// <summary>
    /// Sets an anonymous ambient context for the calling (async) test method until disposed.
    /// Called synchronously, so the AsyncLocal write lands in the caller's execution context.
    /// </summary>
    public static IDisposable AnonymousTenant(string tenantId = "tenant-1")
    {
        BlocksContext.SetContext(AnonymousContext(tenantId), false);
        return new ResetContext();
    }

    private sealed class ResetContext : IDisposable
    {
        public void Dispose() => BlocksContext.SetContext(null, false);
    }
}

/// <summary>Records every log call with its rendered message, structured state and exception.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToList();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(p => p.Key, p => p.Value?.ToString())
            : new Dictionary<string, string?>();
        _entries.Enqueue(new CapturedLogEntry(logLevel, formatter(state, exception), values, exception));
    }
}

internal sealed record CapturedLogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, string?> State, Exception? Exception)
{
    /// <summary>Everything this entry would emit: message, every state value and the exception text.</summary>
    public string AllText =>
        string.Join("\n", new[] { Message, Exception?.ToString() ?? string.Empty }
            .Concat(State.Select(kv => $"{kv.Key}={kv.Value}")));
}
