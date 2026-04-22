namespace BlocksTemplate.DomainService;

public interface IEventService
{
    EventItem Create(CreateEventRequest request);
    EventItem? Update(string id, UpdateEventRequest request);
    PagedEventsDto GetPage(EventListQuery query);
    EventItem? Get(string id);
    bool Delete(string id);
}
