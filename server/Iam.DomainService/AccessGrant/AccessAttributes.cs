using System.Collections;
using System.Text.Json;

namespace Iam.DomainService.AccessGrant
{
    /// <summary>
    /// Reads the org-scoped access attributes data policies use:
    /// <c>User.Attributes["access"][orgId][name] = string[]</c>.
    /// </summary>
    /// <remarks>
    /// Tolerant of every shape the bag can hold -- the CLR maps and lists the attribute serializer
    /// reads back, a raw <see cref="JsonElement"/> from a request body, and a single string where a
    /// list was meant -- because a value this reader skips is a value R10 would never check. Other
    /// keys of <c>Attributes</c> are not read.
    /// </remarks>
    public static class AccessAttributes
    {
        public const string Key = "access";

        /// <summary>orgId → name → values. Empty when the bag holds no access attributes.</summary>
        public static Dictionary<string, Dictionary<string, HashSet<string>>> Read(Dictionary<string, object>? attributes)
        {
            var result = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
            if (attributes is null || !attributes.TryGetValue(Key, out var access))
            {
                return result;
            }

            foreach (var (organizationId, names) in Entries(access))
            {
                var byName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (var (name, values) in Entries(names))
                {
                    byName[name] = new HashSet<string>(Values(values), StringComparer.Ordinal);
                }

                result[organizationId] = byName;
            }

            return result;
        }

        /// <summary>
        /// True when the bag carries an <c>access</c> key whose shape is not orgId → name → values.
        /// Such a value grants nothing today, but it is refused rather than stored for a non-system
        /// caller, so nothing unreadable can sit where a policy will look.
        /// </summary>
        public static bool IsMalformed(Dictionary<string, object>? attributes)
        {
            if (attributes is null || !attributes.TryGetValue(Key, out var access) || access is null)
            {
                return false;
            }

            if (!IsMap(access))
            {
                return true;
            }

            return Entries(access).Any(entry => !IsMap(entry.Value));
        }

        private static bool IsMap(object? value) =>
            value is IDictionary<string, object> || value is JsonElement { ValueKind: JsonValueKind.Object };

        private static IEnumerable<(string Key, object? Value)> Entries(object? value)
        {
            if (value is IDictionary<string, object> map)
            {
                return map.Select(entry => (entry.Key, (object?)entry.Value));
            }

            if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
            {
                return element.EnumerateObject().Select(property => (property.Name, (object?)property.Value)).ToList();
            }

            return [];
        }

        private static List<string> Values(object? value)
        {
            switch (value)
            {
                case null:
                    return [];
                case string text:
                    return [text];
                case JsonElement { ValueKind: JsonValueKind.Array } array:
                    return array.EnumerateArray().Select(item => ToText(item)).OfType<string>().ToList();
                case JsonElement element:
                    var single = ToText(element);
                    return single is null ? [] : [single];
                case IEnumerable sequence:
                    return sequence.Cast<object?>().Select(ToText).OfType<string>().ToList();
                default:
                    return [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty];
            }
        }

        private static string? ToText(object? value) => value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.GetRawText(),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
