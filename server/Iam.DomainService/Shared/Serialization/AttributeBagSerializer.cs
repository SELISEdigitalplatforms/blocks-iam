using System.Collections;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Iam.DomainService.Shared.Serialization
{
    /// <summary>
    /// Serializer for the free-form <c>Dictionary&lt;string, object&gt;</c> attribute bags hanging
    /// off User, Organization and Theme.
    /// <para>
    /// Without it those members fall through to the driver's <c>ObjectSerializer</c>, which Genesis
    /// registers fully permissive (<c>new ObjectSerializer(_ =&gt; true)</c>). ASP.NET binds request
    /// bodies with System.Text.Json, so an <c>object</c> slot arrives holding a
    /// <see cref="JsonElement"/>; the permissive serializer auto-classmaps it and writes
    /// <c>{ "plan": { "_t": "JsonElement" } }</c> - the value silently dropped, only a discriminator
    /// left behind. Reading that document back then throws
    /// <c>Unknown discriminator value 'JsonElement'</c>, and every list endpoint over the collection
    /// returns 500 because one poisoned row fails the whole batch deserialization.
    /// </para>
    /// <para>
    /// So this type does both halves. On write it maps CLR and <see cref="JsonElement"/> values onto
    /// plain BSON and never emits a discriminator, which makes the bad shape unwritable from any
    /// code path. On read it strips <c>_t</c>/<c>_v</c> wrappers instead of resolving them, so rows
    /// already poisoned in an existing database load rather than throw - no migration needed to get
    /// the endpoints back (see <c>scripts/fix-jsonelement-attributes.js</c> to clear the leftovers).
    /// </para>
    /// </summary>
    public sealed class AttributeBagSerializer : SerializerBase<Dictionary<string, object>>
    {
        private const string DiscriminatorField = "_t";
        private const string WrappedValueField = "_v";

        public override Dictionary<string, object> Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var reader = context.Reader;

            if (reader.GetCurrentBsonType() == BsonType.Null)
            {
                reader.ReadNull();
                return new Dictionary<string, object>();
            }

            return ToDictionary(BsonDocumentSerializer.Instance.Deserialize(context));
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Dictionary<string, object> value)
        {
            if (value is null)
            {
                context.Writer.WriteNull();
                return;
            }

            var document = new BsonDocument();

            foreach (var entry in value)
            {
                document[entry.Key] = ToBsonValue(entry.Value);
            }

            BsonDocumentSerializer.Instance.Serialize(context, document);
        }

        private static Dictionary<string, object> ToDictionary(BsonDocument document)
        {
            // A "_t" alongside a "_v" is the driver's wrapper around a value whose runtime type did
            // not match the declared one; the payload is what the caller stored, so unwrap it. A
            // "_t" on its own is the poisoned shape above - dropping it leaves an empty bag, which
            // is exactly what was persisted.
            if (document.Contains(DiscriminatorField) && document.TryGetValue(WrappedValueField, out var wrapped))
            {
                return ToClrValue(wrapped) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }

            var result = new Dictionary<string, object>();

            foreach (var element in document)
            {
                if (element.Name == DiscriminatorField)
                {
                    continue;
                }

                var converted = ToClrValue(element.Value);
                if (converted is not null)
                {
                    result[element.Name] = converted;
                }
            }

            return result;
        }

        private static object? ToClrValue(BsonValue value)
        {
            switch (value.BsonType)
            {
                case BsonType.Document:
                    return ToDictionary(value.AsBsonDocument);

                case BsonType.Array:
                    var items = new List<object>();
                    foreach (var item in value.AsBsonArray)
                    {
                        var converted = ToClrValue(item);
                        if (converted is not null)
                        {
                            items.Add(converted);
                        }
                    }
                    return items;

                case BsonType.Null:
                case BsonType.Undefined:
                    return null;

                default:
                    return BsonTypeMapper.MapToDotNetValue(value);
            }
        }

        private static BsonValue ToBsonValue(object? value)
        {
            switch (value)
            {
                case null:
                    return BsonNull.Value;

                case JsonElement element:
                    return FromJsonElement(element);

                case BsonValue bson:
                    return bson;

                case string text:
                    return new BsonString(text);

                case IDictionary<string, object> dictionary:
                    var document = new BsonDocument();
                    foreach (var entry in dictionary)
                    {
                        document[entry.Key] = ToBsonValue(entry.Value);
                    }
                    return document;

                // Guarded by the string case above, which would otherwise match here as IEnumerable<char>.
                case IEnumerable enumerable:
                    var array = new BsonArray();
                    foreach (var item in enumerable)
                    {
                        array.Add(ToBsonValue(item));
                    }
                    return array;
            }

            try
            {
                return BsonValue.Create(value);
            }
            catch (ArgumentException)
            {
                // Anything the driver has no BSON mapping for. Storing its text beats storing a
                // discriminator that cannot be read back.
                return new BsonString(value.ToString() ?? string.Empty);
            }
        }

        private static BsonValue FromJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return new BsonString(element.GetString() ?? string.Empty);

                case JsonValueKind.Number:
                    // Integers stay integers: without the explicit Int64 branch every whole number
                    // round-trips as 10.0 instead of 10.
                    return element.TryGetInt64(out var whole) ? new BsonInt64(whole) : new BsonDouble(element.GetDouble());

                case JsonValueKind.True:
                    return BsonBoolean.True;

                case JsonValueKind.False:
                    return BsonBoolean.False;

                case JsonValueKind.Object:
                    var document = new BsonDocument();
                    foreach (var property in element.EnumerateObject())
                    {
                        document[property.Name] = FromJsonElement(property.Value);
                    }
                    return document;

                case JsonValueKind.Array:
                    var array = new BsonArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        array.Add(FromJsonElement(item));
                    }
                    return array;

                default:
                    return BsonNull.Value;
            }
        }
    }
}
