using System.Collections;
using System.Text.Json;

namespace Iam.DomainService.Shared.Serialization
{
    /// <summary>
    /// Size and shape limits applied to a free-form attribute bag. The type rules are identical
    /// everywhere; only the caps differ, because the surfaces differ in who can reach them.
    /// </summary>
    public sealed record AttributePolicy(int MaxKeys, int MaxKeyLength, int MaxStringLength, int MaxDepth, int MaxArrayItems)
    {
        /// <summary>
        /// Anonymous surfaces - signup. These caps are a security control, not a preference: signup
        /// is an unauthenticated write into the tenant database, so the bag stays small.
        /// </summary>
        public static readonly AttributePolicy Public = new(MaxKeys: 25, MaxKeyLength: 64, MaxStringLength: 512, MaxDepth: 3, MaxArrayItems: 25);

        /// <summary>
        /// Authenticated surfaces - the admin and Construct endpoints. Deliberately generous: these
        /// caps exist to stop the bag being used as a document store, not to police business data,
        /// so hitting one should be rare enough to treat as a bug in the caller.
        /// </summary>
        public static readonly AttributePolicy Internal = new(MaxKeys: 100, MaxKeyLength: 128, MaxStringLength: 8192, MaxDepth: 5, MaxArrayItems: 100);
    }

    /// <summary>
    /// Converts a client-supplied attribute bag into values the MongoDB driver can persist, and
    /// caps its size.
    /// <para>
    /// ASP.NET binds request bodies with System.Text.Json, so the values of a
    /// <c>Dictionary&lt;string, object&gt;</c> arrive as <see cref="JsonElement"/> rather than as
    /// string/long/bool. Left alone those reach the driver as an unknown type and persist as
    /// <c>{ "plan": { "_t": "JsonElement" } }</c> - the value silently lost. Every bag bound from a
    /// request body has to come through here first.
    /// </para>
    /// <para>
    /// <see cref="AttributeBagSerializer"/> is the backstop for the same problem at the driver
    /// boundary, so a call site that forgets this class still cannot write the broken shape. This
    /// class is what applies the caps and key rules, which the serializer deliberately does not.
    /// </para>
    /// </summary>
    public static class AttributeNormalizer
    {
        public static Dictionary<string, object> Normalize(Dictionary<string, object>? raw, AttributePolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);

            return NormalizeMap(raw, policy, depth: 1);
        }

        private static Dictionary<string, object> NormalizeMap(IEnumerable<KeyValuePair<string, object>>? raw, AttributePolicy policy, int depth)
        {
            var result = new Dictionary<string, object>();

            if (raw is null)
            {
                return result;
            }

            foreach (var entry in raw)
            {
                if (result.Count >= policy.MaxKeys)
                {
                    break;
                }

                if (!IsValidKey(entry.Key, policy))
                {
                    continue;
                }

                var value = Convert(entry.Value, policy, depth);
                if (value is not null)
                {
                    result[entry.Key] = value;
                }
            }

            return result;
        }

        // Mongo rejects field names starting with '$', and a '.' would be read as a path separator
        // on query, silently addressing a nested field that does not exist.
        private static bool IsValidKey(string? key, AttributePolicy policy)
        {
            return !string.IsNullOrWhiteSpace(key)
                   && key.Length <= policy.MaxKeyLength
                   && !key.StartsWith('$')
                   && !key.Contains('.');
        }

        /// <param name="depth">Nesting level of the container holding this value; the bag itself is 1.</param>
        private static object? Convert(object? value, AttributePolicy policy, int depth)
        {
            if (value is JsonElement element)
            {
                return ConvertJson(element, policy, depth);
            }

            // Server-built bags (the SSO mappers) already hold CLR values, but the caps still apply:
            // they guard the size of the write, not just the shape of the input.
            return value switch
            {
                null => null,
                string text => Truncate(text, policy),
                IDictionary<string, object> map => CanNest(policy, depth) ? NormalizeMap(map, policy, depth + 1) : null,
                IEnumerable sequence => CanNest(policy, depth) ? ConvertSequence(sequence, policy, depth + 1) : null,
                _ => value,
            };
        }

        // A container one level below `depth` is dropped whole rather than stored empty, so nothing
        // is persisted in a shape the caller did not ask for.
        private static bool CanNest(AttributePolicy policy, int depth) => depth + 1 <= policy.MaxDepth;

        private static object? ConvertJson(JsonElement element, AttributePolicy policy, int depth)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return Truncate(element.GetString(), policy);

                case JsonValueKind.Number:
                    // The (object) cast is load-bearing: without it C# unifies the two ternary
                    // branches to double and every integer persists as 10.0 instead of 10.
                    return element.TryGetInt64(out var whole) ? (object)whole : element.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Object:
                    if (!CanNest(policy, depth))
                    {
                        return null;
                    }

                    var map = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        if (map.Count >= policy.MaxKeys)
                        {
                            break;
                        }

                        if (!IsValidKey(property.Name, policy))
                        {
                            continue;
                        }

                        var converted = Convert(property.Value, policy, depth + 1);
                        if (converted is not null)
                        {
                            map[property.Name] = converted;
                        }
                    }
                    return map;

                case JsonValueKind.Array:
                    return CanNest(policy, depth) ? ConvertJsonArray(element, policy, depth + 1) : null;

                default:
                    // Null and Undefined: dropped rather than stored as BsonNull.
                    return null;
            }
        }

        private static List<object> ConvertJsonArray(JsonElement element, AttributePolicy policy, int depth)
        {
            var items = new List<object>();

            foreach (var item in element.EnumerateArray())
            {
                if (items.Count >= policy.MaxArrayItems)
                {
                    break;
                }

                var converted = Convert(item, policy, depth);
                if (converted is not null)
                {
                    items.Add(converted);
                }
            }

            return items;
        }

        private static List<object> ConvertSequence(IEnumerable sequence, AttributePolicy policy, int depth)
        {
            var items = new List<object>();

            foreach (var item in sequence)
            {
                if (items.Count >= policy.MaxArrayItems)
                {
                    break;
                }

                var converted = Convert(item, policy, depth);
                if (converted is not null)
                {
                    items.Add(converted);
                }
            }

            return items;
        }

        private static string? Truncate(string? value, AttributePolicy policy)
        {
            if (value is null)
            {
                return null;
            }

            return value.Length > policy.MaxStringLength ? value[..policy.MaxStringLength] : value;
        }
    }
}
