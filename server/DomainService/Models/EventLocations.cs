namespace BlocksTemplate.DomainService;

/// <summary>
/// Allowed event venues for validation and UI dropdowns.
/// </summary>
public static class EventLocations
{
    public static readonly IReadOnlyList<string> All =
    [
        "Online",
        "HQ — Main auditorium",
        "HQ — Workshop room",
        "City convention center",
        "Partner venue — Downtown",
        "Outdoor plaza",
    ];

    public static bool IsAllowed(string location) =>
        All.Contains(location, StringComparer.Ordinal);
}
