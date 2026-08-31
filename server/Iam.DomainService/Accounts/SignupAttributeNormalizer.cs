using System.Text.Json;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// Converts a client-supplied attribute bag into values the MongoDB driver can actually
    /// persist, and caps its size.
    /// <para>
    /// ASP.NET binds request bodies with System.Text.Json, so the values of a
    /// <c>Dictionary&lt;string, object&gt;</c> arrive as <see cref="JsonElement"/> rather than
    /// as string/long/bool. Genesis registers a fully permissive object serializer at startup
    /// (<c>new ObjectSerializer(_ =&gt; true)</c>), so instead of throwing on that unknown type
    /// the driver auto-classmaps it — and <see cref="JsonElement"/> exposes only
    /// <c>ValueKind</c>, which the driver skips. The document is then written as
    /// <c>{ "plan": { "_t": "JsonElement" } }</c>: every value silently lost, no exception and
    /// no log. Everything reaching an entity has to come through here first.
    /// </para>
    /// <para>
    /// The caps are not cosmetic. Signup is an anonymous endpoint, so this is an
    /// unauthenticated write into the tenant database.
    /// </para>
    /// </summary>
    public static class SignupAttributeNormalizer
    {
        private const int MaxKeys = 25;
        private const int MaxKeyLength = 64;
        private const int MaxStringLength = 512;

        public static Dictionary<string, object> Normalize(Dictionary<string, object>? raw)
        {
            var result = new Dictionary<string, object>();

            if (raw is null)
            {
                return result;
            }

            foreach (var entry in raw)
            {
                if (result.Count >= MaxKeys)
                {
                    break;
                }

                if (!IsValidKey(entry.Key))
                {
                    continue;
                }

                var value = Convert(entry.Value);
                if (value is not null)
                {
                    result[entry.Key] = value;
                }
            }

            return result;
        }

        // Mongo rejects field names starting with '$', and a '.' would be read as a path
        // separator on query, silently addressing a nested field that does not exist.
        private static bool IsValidKey(string? key)
        {
            return !string.IsNullOrWhiteSpace(key)
                   && key.Length <= MaxKeyLength
                   && !key.StartsWith('$')
                   && !key.Contains('.');
        }

        private static object? Convert(object? value)
        {
            // Server-built bags (the SSO mappers) already hold CLR values, so only bodies bound
            // from JSON need converting — but the length cap still applies to them, since it
            // guards the size of the write rather than the shape of the input.
            if (value is not JsonElement element)
            {
                return value is string clrString ? Truncate(clrString) : value;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return Truncate(element.GetString());

                case JsonValueKind.Number:
                    // The (object) cast is load-bearing: without it C# unifies the two ternary
                    // branches to double and every integer persists as 10.0 instead of 10.
                    return element.TryGetInt64(out var whole) ? (object)whole : element.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Object:
                    // A nested Dictionary/BsonDocument in an object slot picks up _t/_v
                    // discriminator wrappers from the permissive serializer above, which turns
                    // Attributes.meta.region into Attributes.meta._v.region and breaks dotted
                    // queries. Keeping the raw JSON is lossless and stays queryable as a value.
                    return Truncate(element.GetRawText());

                case JsonValueKind.Array:
                    return ConvertArray(element);

                default:
                    // Null and Undefined: dropped rather than stored as BsonNull.
                    return null;
            }
        }

        private static List<object> ConvertArray(JsonElement element)
        {
            var items = new List<object>();

            foreach (var item in element.EnumerateArray())
            {
                if (items.Count >= MaxKeys)
                {
                    break;
                }

                // Nesting inside arrays carries the same discriminator problem and has no
                // clean raw-JSON equivalent at this position, so it is skipped outright.
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    continue;
                }

                var converted = Convert(item);
                if (converted is not null)
                {
                    items.Add(converted);
                }
            }

            return items;
        }

        private static string? Truncate(string? value)
        {
            if (value is null)
            {
                return null;
            }

            return value.Length > MaxStringLength ? value[..MaxStringLength] : value;
        }
    }
}
