using System.Text.Json.Serialization.Metadata;
using Blocks.Genesis.Entities.Iam;

namespace Api.Security;

/// <summary>
/// Keeps client secrets out of every API response. When a client credential, OIDC client or
/// identity provider is written as JSON, its secret properties are left out entirely (no key,
/// not even null), whatever endpoint returns it. A new secret is returned only by create and
/// rotate-secret, through their own response classes, which this does not touch.
/// Only writing is affected: request bodies are read as before.
/// </summary>
public static class SecretFieldsJsonModifier
{
    private static readonly Dictionary<Type, string[]> SecretProperties = new()
    {
        [typeof(ClientCredential)] = [nameof(ClientCredential.ClientSecret), nameof(ClientCredential.ClientSecretReference)],
        [typeof(OidcClientRegistration)] = [nameof(OidcClientRegistration.ClientSecret)],
        [typeof(IdentityProvider)] =
        [
            nameof(IdentityProvider.ClientSecret),
            nameof(IdentityProvider.PrivateKey),
            nameof(IdentityProvider.ClientSecretReference),
            nameof(IdentityProvider.PrivateKeyReference),
        ],
    };

    public static void Apply(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !TryGetSecrets(typeInfo.Type, out var secrets))
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            var member = (property.AttributeProvider as System.Reflection.MemberInfo)?.Name;
            if (member != null && secrets.Contains(member, StringComparer.Ordinal))
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }

    // A subclass of one of these entities carries the same secret, so it is covered too.
    private static bool TryGetSecrets(Type type, out string[] secrets)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (SecretProperties.TryGetValue(t, out secrets!))
            {
                return true;
            }
        }

        secrets = [];
        return false;
    }
}
