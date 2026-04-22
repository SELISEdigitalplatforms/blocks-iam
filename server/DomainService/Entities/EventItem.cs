using MongoDB.Bson.Serialization.Attributes;

namespace BlocksTemplate.DomainService;

public sealed record EventItem(
    [property: BsonId] string Id,
    string Name,
    DateTimeOffset StartDateTime,
    DateTimeOffset EndDateTime,
    string Description,
    string Location,
    string Organizer);