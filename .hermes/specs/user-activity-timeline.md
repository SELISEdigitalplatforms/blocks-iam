# User Activity Timeline — Specification

## Overview & Goals

A cross-user, dashboard-level timeline that aggregates all user activity across the IAM system into a single chronological, filterable view. This is distinct from the existing per-user History/Devices tabs — those show one user at a time. The Activity Timeline provides an admin/operator with system-wide visibility into authentication events, user lifecycle changes, permission mutations, and session activity.

### Goals
- **Single pane of glass** for all user activity across tenants/organizations
- **Real-time-ish** — events appear within seconds via SignalR or polling
- **Filterable** by event type, user, organization, time range, IP, device
- **Exportable** to CSV for audit/compliance
- **Zero new database collections** — reuse existing `Sessions`, `UserAuthenticationTimelines`, and `UserTimelines`

### Non-Goals
- Replacing per-user History/Devices tabs (those remain for user-specific deep dives)
- Real-time streaming with sub-second latency (near-real-time is sufficient)
- Historical data migration (only future + existing data is surfaced)

---

## Architecture & Data Model

### Data Sources (existing, no new collections)

| Collection | Events represented | Existing API |
|---|---|---|
| `UserAuthenticationTimelines` | Auth events (login via password/social/MFA/SSO, logout, token refresh/revoke, session revoke) | `GET /api/iam/history` |
| `Sessions` | Active device sessions | `GET /api/iam/sessions` |
| `UserTimelines` | User CRUD events (created, updated, deactivated, roles/permissions changed) | `GET /api/iam/users/timeline` |

### Unified Event Model

All events are normalized into a common shape for the timeline:

```typescript
interface ActivityEvent {
  id: string;                    // Composite: "{source}:{documentId}"
  eventType: ActivityEventType;
  userId: string;
  userName: string;              // Denormalized for display (firstName + lastName)
  userEmail: string;
  organizationId: string;
  timestamp: string;             // ISO 8601 UTC
  ipAddress: string;
  deviceInfo: DeviceInformation;
  metadata: Record<string, unknown>;  // Event-specific payload
}

type ActivityEventType =
  // Auth events (from UserAuthenticationTimelines)
  | "login_via_password"
  | "login_via_social"
  | "login_via_mfa_code"
  | "login_via_sso_consent"
  | "login_via_authorization_code"
  | "logout"
  | "logout_all"
  | "session_revoked"
  | "token_renewed"
  | "refresh_token_issued"
  | "refresh_token_revoked"
  // User lifecycle events (from UserTimelines)
  | "user_created"
  | "user_updated"
  | "user_deactivated"
  | "user_activated"
  | "user_email_verified"
  | "user_mfa_enabled"
  | "user_mfa_disabled"
  | "user_roles_changed"
  | "user_permissions_changed";
```

### API Endpoint (new)

```
GET /api/iam/activity/timeline
```

Security: `[Authorize]` + `[ProtectedEndPoint("blocks-idp::get-activity-timeline")]`

---

## API Contract

### Request

```
GET /api/iam/activity/timeline?page=0&pageSize=20&sort=timestamp:desc
```

| Query Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `page` | int | No | 0 | Zero-based page |
| `pageSize` | int | No | 20 | 5–100, clamped |
| `sort.property` | string | No | `timestamp` | `timestamp` or `eventType` |
| `sort.isDescending` | bool | No | `true` | |
| `filter.userId` | string | No | — | Single user |
| `filter.organizationId` | string | No | — | Org-scoped |
| `filter.eventTypes` | string[] | No | — | CSV of event types |
| `filter.dateFrom` | ISO 8601 | No | — | Inclusive |
| `filter.dateTo` | ISO 8601 | No | — | Inclusive |
| `filter.ipAddress` | string | No | — | Partial match |
| `filter.search` | string | No | — | Free-text across userName/email/ip |

### Response

```json
{
  "data": [
    {
      "id": "auth:abc123",
      "eventType": "login_via_password",
      "userId": "user-456",
      "userName": "Jane Doe",
      "userEmail": "jane@example.com",
      "organizationId": "org-789",
      "timestamp": "2026-06-24T10:30:00Z",
      "ipAddress": "192.168.1.100",
      "deviceInfo": {
        "Browser": "Chrome",
        "OS": "Windows",
        "Device": "Desktop",
        "Brand": "",
        "Model": ""
      },
      "metadata": {
        "success": true,
        "mfaUsed": false
      }
    }
  ],
  "totalCount": 1542,
  "errors": null
}
```

### Export Endpoint

```
GET /api/iam/activity/timeline/export
```

Same filters as above. Returns `text/csv` with Content-Disposition attachment. Max 10,000 rows per export.

---

## Backend Implementation

### New files (`server/Iam.DomainService/Activities/`)

```
Activities/
├── Services/
│   ├── IActivityTimelineService.cs      # Interface
│   └── ActivityTimelineService.cs       # Aggregation logic
├── ActivityTimelineRequest.cs           # Query model (extends BaseGetsRequest)
├── ActivityTimelineResponse.cs          # Response model
├── ActivityEvent.cs                     # Unified event model
└── ActivityEventType.cs                 # Enum
```

### Controller addition (`server/Api/Controllers/IamController.cs`)

```csharp
[HttpGet("activity/timeline")]
[Authorize]
[ProtectedEndPoint("blocks-idp::get-activity-timeline")]
public async Task<ActivityTimelineResponse> GetActivityTimeline(
    [FromQuery] ActivityTimelineRequest query)
{
    return await _activityTimelineService.GetTimelineAsync(query);
}

[HttpGet("activity/timeline/export")]
[Authorize]
[ProtectedEndPoint("blocks-idp::get-activity-timeline")]
public async Task<IActionResult> ExportActivityTimeline(
    [FromQuery] ActivityTimelineRequest query)
{
    var csv = await _activityTimelineService.ExportCsvAsync(query);
    return File(Encoding.UTF8.GetBytes(csv), "text/csv", 
        $"activity-timeline-{DateTime.UtcNow:yyyyMMdd}.csv");
}
```

### Aggregation Strategy

`ActivityTimelineService` queries all three collections in parallel, merges results, sorts by timestamp, and applies pagination. MongoDB aggregation pipeline with `$unionWith` across the three collections when they share a compatible shape; otherwise, three separate queries merged in-memory with LINQ.

```
┌──────────────────────────┐
│ UserAuthenticationTimelines │──┐
└──────────────────────────┘  │
┌──────────┐                  ├──► ActivityTimelineService ──► API
│ Sessions │──────────────────│       merge → sort → page
└──────────┘                  │
┌──────────────┐              │
│ UserTimelines │─────────────┘
└──────────────┘
```

---

## Frontend Implementation

### Route

`/app/iam/activity` — listed in the IAM dashboard sidebar under "Activity Timeline"

### New files (`client/app/idp/iam/`)

```
modules/user-management/
├── activity-timeline/
│   ├── activity-timeline.tsx           # Page component (card + filters + table)
│   ├── activity-timeline-table.tsx     # React Table with columns
│   ├── activity-timeline-filters.tsx   # Filter bar (date range, event type, user search, org)
│   ├── activity-event-badge.tsx        # Color-coded event type pill
│   └── index.ts
hooks/
├── use-activity-timeline.ts            # React Query hook for GET /api/iam/activity/timeline
models/
├── activity.ts                         # ActivityEvent, ActivityTimelineRequest, etc.
services/
├── activity.service.ts                 # HTTP client methods
constants/
├── activity-constants.ts               # Event type labels, colors, icons
```

### Page Component Layout

```
┌──────────────────────────────────────────────────────┐
│  Activity Timeline                    [Export CSV]   │
│                                                      │
│  ┌─────────────────────────────────────────────────┐ │
│  │ [Date Range ▼] [Event Type ▼] [Search...]  [🔍]│ │
│  │ [Organization ▼] [IP Address...]                │ │
│  └─────────────────────────────────────────────────┘ │
│                                                      │
│  ┌──────┬──────────┬────────┬──────────┬───────────┐│
│  │ Type │ User     │ Event  │ Time     │ IP/Device ││
│  ├──────┼──────────┼────────┼──────────┼───────────┤│
│  │ 🔑   │ Jane Doe │ Login  │ 2 min ago│ 10.0.0.1  ││
│  │      │ jane@... │ via PW │          │ Chrome/Win││
│  ├──────┼──────────┼────────┼──────────┼───────────┤│
│  │ 👤   │ Bob S.   │ Created│ 1 hr ago │ —         ││
│  │      │ bob@...  │        │          │           ││
│  └──────┴──────────┴────────┴──────────┴───────────┘│
│                                                      │
│  < 1 2 3 ... 15 >  (20 per page)                    │
└──────────────────────────────────────────────────────┘
```

### Event Type Color Coding (Badge component)

| Category | Color | Types |
|---|---|---|
| Authentication | Blue | login_via_*, token_*, refresh_token_* |
| Session | Orange | session_revoked, logout, logout_all |
| User Lifecycle | Green | user_created, user_updated, user_activated |
| Security | Red | user_deactivated, user_mfa_* |
| Permissions | Purple | user_roles_changed, user_permissions_changed |

### React Query Hook

```typescript
export const useGetActivityTimeline = (filters: ActivityTimelineRequest) => {
  return useQuery({
    queryKey: ["activity-timeline", filters],
    queryFn: () => activityService.getTimeline(filters),
    placeholderData: keepPreviousData,
  });
};
```

### Client-side caching

- Refetch on window focus (staleTime: 30s) for near-real-time feel
- Pagination uses `keepPreviousData` to avoid flash on page change

---

## Error Handling

| Scenario | Behavior |
|---|---|
| No events in range | Empty state with illustration + "No activity found for this period" |
| API error (5xx) | Toast: "Failed to load activity. Retrying..." + retry button |
| Network timeout | Skeleton loading → error state after 10s |
| Export > 10,000 rows | Toast: "Export limited to 10,000 rows. Narrow your filters." |
| Empty search results | "No results matching '{query}'" |

---

## Security

- Endpoint protected by `[Authorize]` + permission `blocks-idp::get-activity-timeline`
- Organization-scoped: users only see activity for orgs they belong to (enforced server-side)
- IP addresses displayed but never used as PII identifiers in exports (mask last octet in CSV: `192.168.1.XXX`)
- No session token exposure in timeline events

---

## Testing Plan

### Backend (xUnit)

| Test | Coverage |
|---|---|
| `ActivityTimelineService_WithValidFilters_ReturnsMergedEvents` | Happy path |
| `ActivityTimelineService_WithDateRange_FiltersCorrectly` | Date filtering |
| `ActivityTimelineService_WithOrgScope_EnforcesTenantIsolation` | Multi-tenancy |
| `ActivityTimelineService_WithNoResults_ReturnsEmptyList` | Empty state |
| `ActivityTimelineService_ExportCsv_ReturnsValidCsv` | Export format |
| `ActivityTimelineService_ExportCsv_Respects10000RowLimit` | Export cap |
| `ActivityTimelineService_SortsByTimestamp` | Sort order |
| `ActivityTimelineController_Unauthenticated_Returns401` | Auth guard |
| `ActivityTimelineController_Unauthorized_Returns403` | Permission guard |

### Frontend (Vitest + React Testing Library)

| Test | Coverage |
|---|---|
| Renders timeline with mock data | Happy path |
| Filters update query params and refetch | Filter interaction |
| Pagination controls work | Pagination |
| Empty state renders correctly | Empty |
| Error state with retry button | Error |
| Export button triggers download | Export |
| Event badges render correct colors | Visual |

---

## Deployment & Rollout

1. **Backend first** — deploy new endpoints behind feature flag `activity_timeline_enabled` (off by default)
2. **Smoke test** in staging with a small tenant
3. **Enable frontend** route behind same feature flag
4. **Monitor** MongoDB query performance on the three collections (add compound indexes if needed: `{UserId: 1, CreatedDate: -1}`)
5. **Roll out** to all tenants, remove feature flag after 1 week of stable operation
6. **Add permission** `blocks-idp::get-activity-timeline` to admin roles

### MongoDB Indexes (add if query performance degrades)

```javascript
// On UserAuthenticationTimelines
db.UserAuthenticationTimelines.createIndex({ "UserId": 1, "CreatedDate": -1 })
db.UserAuthenticationTimelines.createIndex({ "OrganizationIds": 1, "CreatedDate": -1 })

// On UserTimelines  
db.UserTimelines.createIndex({ "UserId": 1, "CreatedDate": -1 })
```

---

## Dependencies

- **Existing services**: `IUserActivityRepository`, `IUserRepository` (already registered in DI)
- **Existing UI kit**: `@seliseblocks/blocks-kit`, `@tanstack/react-table`, `@tanstack/react-query`
- **Existing patterns**: Follows `UserActivityService` / `UserActivityRepository` pattern, `BaseGetsRequest` pagination, `UserDevices`/`UserHistories` component structure
- **No new NuGet/npm packages required**

## References

- Existing `GET /api/iam/history` — auth event retrieval
- Existing `GET /api/iam/sessions` — session retrieval
- Existing `GET /api/iam/users/timeline` — user timeline retrieval
- Existing `UserHistoryList` component — table pattern to follow
- Existing `UserDevices` component — card + table + pagination pattern