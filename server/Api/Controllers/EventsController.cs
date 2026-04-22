using BlocksTemplate.DomainService;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers;

[ApiController]
[Route("events")]
public sealed class EventsController(IEventService events) : ControllerBase
{
    [HttpGet("locations")]
    public ActionResult<IReadOnlyList<string>> GetLocations() =>
        Ok(EventLocations.All);

    [HttpPost]
    public ActionResult<EventItem> Create([FromBody] CreateEventRequest request)
    {
        var created = events.Create(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public ActionResult<EventItem> Update(string id, [FromBody] UpdateEventRequest request)
    {
        var updated = events.Update(id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet]
    public ActionResult<PagedEventsDto> GetAll([FromQuery] EventListQuery query) =>
        Ok(events.GetPage(query));

    [HttpGet("{id:guid}")]
    public ActionResult<EventItem> GetById(string id)
    {
        var e = events.Get(id);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(string id) =>
        events.Delete(id) ? NoContent() : NotFound();
}
