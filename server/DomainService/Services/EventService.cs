using System.Text.RegularExpressions;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BlocksTemplate.DomainService;

public sealed class EventService(IDbContextProvider db) : IEventService
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int MinFilterLength = 3;
    private const string CollectionName = "template_events";

    private readonly IMongoCollection<EventItem> _collection = db.GetCollection<EventItem>(CollectionName);

    public EventItem Create(CreateEventRequest request)
    {
        var item = new EventItem(
            Guid.NewGuid().ToString(),
            request.Name.Trim(),
            request.StartDateTime,
            request.EndDateTime,
            request.Description.Trim(),
            request.Location.Trim(),
            request.Organizer.Trim());
        _collection.InsertOne(item);
        return item;
    }

    public EventItem? Update(string id, UpdateEventRequest request)
    {
        var updated = new EventItem(
            id,
            request.Name.Trim(),
            request.StartDateTime,
            request.EndDateTime,
            request.Description.Trim(),
            request.Location.Trim(),
            request.Organizer.Trim());
        var result = _collection.ReplaceOne(e => e.Id == id, updated);
        return result.MatchedCount > 0 ? updated : null;
    }

    public EventItem? Get(string id) =>
        _collection.Find(e => e.Id == id).Limit(1).FirstOrDefault();

    public bool Delete(string id) =>
        _collection.DeleteOne(e => e.Id == id).DeletedCount > 0;

    public PagedEventsDto GetPage(EventListQuery query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < MinPageSize ? 10 : query.PageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        var filter = BuildFilter(query);
        var sort = BuildSort(query);

        var total = (int)_collection.CountDocuments(filter);
        var items = _collection
            .Find(filter)
            .Sort(sort)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToList();

        return new PagedEventsDto(items, total, page, pageSize);
    }

    private static FilterDefinition<EventItem> BuildFilter(EventListQuery query)
    {
        var builder = Builders<EventItem>.Filter;
        var filter = builder.Empty;

        if (!string.IsNullOrWhiteSpace(query.Search) && query.Search.Trim().Length >= MinFilterLength)
        {
            var pattern = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filter &= builder.Or(
                builder.Regex(e => e.Name, pattern),
                builder.Regex(e => e.Location, pattern),
                builder.Regex(e => e.Organizer, pattern));
        }

        if (query.RangeStart.HasValue || query.RangeEnd.HasValue)
        {
            var rs = query.RangeStart ?? DateTimeOffset.MinValue;
            var re = query.RangeEnd ?? DateTimeOffset.MaxValue;
            filter &= builder.Lt(e => e.StartDateTime, re) & builder.Gt(e => e.EndDateTime, rs);
        }

        return filter;
    }

    private static SortDefinition<EventItem> BuildSort(EventListQuery query)
    {
        var sortBy = (query.SortBy ?? "name").Trim().ToLowerInvariant();
        var desc = string.Equals(query.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy switch
        {
            "start" or "startdatetime" => desc
                ? Builders<EventItem>.Sort.Descending(e => e.StartDateTime)
                : Builders<EventItem>.Sort.Ascending(e => e.StartDateTime),
            "end" or "enddatetime" => desc
                ? Builders<EventItem>.Sort.Descending(e => e.EndDateTime)
                : Builders<EventItem>.Sort.Ascending(e => e.EndDateTime),
            _ => desc
                ? Builders<EventItem>.Sort.Descending(e => e.Name)
                : Builders<EventItem>.Sort.Ascending(e => e.Name),
        };
    }
}
