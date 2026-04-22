namespace BlocksTemplate.DomainService;

/// <summary>
/// Query parameters for listing events. <see cref="Search"/> matches name, location, or organizer (substring) only when it has at least 3 characters.
/// </summary>
public sealed class EventListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    /// <summary>name | start | end</summary>
    public string? SortBy { get; set; } = "name";

    /// <summary>asc | desc</summary>
    public string? SortDir { get; set; } = "asc";

    /// <summary>Substring match against name, location, or organizer (ignored if fewer than 3 characters).</summary>
    public string? Search { get; set; }
    public DateTimeOffset? RangeStart { get; set; }
    public DateTimeOffset? RangeEnd { get; set; }
}
