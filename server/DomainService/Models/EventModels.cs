using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BlocksTemplate.DomainService;

public sealed record CreateEventRequest(
    string Name,
    DateTimeOffset StartDateTime,
    DateTimeOffset EndDateTime,
    string Description,
    string Location,
    string Organizer);

public sealed record UpdateEventRequest(
    string Name,
    DateTimeOffset StartDateTime,
    DateTimeOffset EndDateTime,
    string Description,
    string Location,
    string Organizer);

public sealed record PagedEventsDto(
    IReadOnlyList<EventItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
