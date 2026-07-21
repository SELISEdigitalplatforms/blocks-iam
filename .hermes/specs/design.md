# design.md — Activity Timeline Frontend Design Specification

## Overview

Cross-user, dashboard-level activity timeline aggregating authentication events, user lifecycle changes, session activity, and permission mutations. Three existing MongoDB collections feed into a single filterable, paginated view.

### Goals
- Single pane of glass for all user activity across tenants
- Filterable by event type, user, organization, date range, IP, search
- Exportable to CSV
- Zero new backend endpoints (reuses existing `/api/iam/sessions`, `/api/iam/history`, `/api/iam/users/timeline`)

---

## User Flows

### Primary: Admin Operator Monitoring
1. Navigate to `/app/iam/activity` from IAM sidebar
2. Scan recent events in table (default: last 7 days, all events)
3. Click stat pill to filter (e.g., "Logins")
4. Type in search field to narrow by user/IP
5. Click row to expand detail panel
6. Click "Export CSV" for audit record

### Secondary: Investigation
1. Set date range to specific incident window
2. Filter by event type (e.g., "Security" events)
3. Search for affected user email
4. Expand rows to see full event detail
5. Copy event ID for reference

---

## Screen: Activity Timeline

### Route
`/app/iam/activity` — under `DashboardLayout`

### Component Hierarchy
```
ActivityTimeline (page)
├── PageBreadcrumb
├── ActivityHeader (title + Export button)
├── ActivityStatsRow (stat pills: Logins, MFA, Failures, Changes)
├── ActivityFilterBar
│   ├── SearchInput
│   ├── Select (date range)
│   ├── Select (event type)
│   ├── Select (organization)
│   └── Button (reset)
├── ActivityLiveIndicator (conditional: new events)
├── Card
│   ├── CardContent
│   │   ├── Table (React Table)
│   │   │   ├── TableHeader
│   │   │   └── TableBody (rows)
│   │   └── Pagination
└── ExportDialog (conditional)
```

### Reused Components

| Component | Source |
|-----------|--------|
| `Card`, `CardContent` | `@/components/ui-kits/card/card` |
| `Table`, `TableHeader`, `TableBody`, `TableRow`, `TableHead`, `TableCell` | `@/components/ui-kits/table/table` |
| `Badge` | `@/components/ui-kits/badge/badge` |
| `Button` | `@/components/ui-kits/button/button` |
| `Input` | `@/components/ui-kits/input/input` |
| `Select` | `@/components/ui-kits/select/select` |
| `Skeleton` | `@/components/ui-kits/skeleton/skeleton` |
| `Pagination` | `@/components/ui-kits/pagination/pagination` |
| `Dialog` | `@/components/ui-kits/dialog/dialog` (for export) |
| `Tooltip` | `@/components/ui-kits/tooltip/tooltip` |
| `PageBreadcrumb` | `@seliseblocks/blocks-kit` |
| `useReactTable` | `@tanstack/react-table` |
| `formatDistanceToNow` | `date-fns` |
| `useQueryStates` | `nuqs` |

### New Components
None required. All UI composed from existing components.

### Design Tokens Used
All colors/sizing from `globals.css` CSS variables. No hardcoded values.

### Typography
- `font-sans`: DM Sans (from `--font-sans`)
- Page heading: `text-2xl font-bold tracking-tight`
- Column headers: `text-xs font-semibold text-medium-emphasis uppercase tracking-[0.5px]`
- Table body: `text-sm`
- Time values: `font-mono` (SF Mono)

### Event Type Color System

| Category | Token Used | Badge Style |
|----------|-----------|-------------|
| Authentication | `--chart-blue` | `bg-blue-500/10 text-blue-500` |
| MFA | `--chart-purple` | `bg-purple-500/10 text-purple-500` |
| Tokens | `--chart-orange` | `bg-orange-500/10 text-orange-500` |
| Security | `--error` | `bg-red-500/10 text-red-500` |
| Lifecycle | `--success` | `bg-green-500/10 text-green-500` |
| Permissions | `--primary` | `bg-indigo-500/10 text-indigo-500` |

---

## States

### Loading
`<Skeleton className="h-12 w-full rounded-xl"/>` × 10 rows. Matches `UserHistoryList` pattern exactly.

### Empty (Initial)
```
┌──────────────────────────────────────┐
│        [Activity icon — 64px]        │
│     No activity recorded yet          │
│  Activity appears as users interact   │
└──────────────────────────────────────┘
```

### Empty (Filtered)
```
┌──────────────────────────────────────┐
│        [Search icon — 64px]          │
│  No events matching your filters     │
│           [Clear all filters]         │
└──────────────────────────────────────┘
```

### Error
Toast: "Failed to load activity. Retrying..." with retry button.

### Success
Export toast: "Exported N events to CSV"

---

## Responsive Behavior

### Desktop (≥1024px)
Full layout: Breadcrumb → Header → Stats → Filters → Table → Pagination

### Tablet (640px–1023px)
- Stats wrap to 2 rows
- Filters stack vertically
- Table columns condense (When + Details merge)
- Export becomes icon-only

### Mobile (<640px)
- Stats → horizontal scrollable chips
- Filters → `<Sheet>` bottom drawer triggered by floating "Filters (N)" button
- Table → card list (each row becomes a `<Card>`)
- Pagination → "Load more" button
- Touch targets ≥44px

---

## Accessibility
- `<Table>` provides semantic structure with `<thead>`, `<tbody>`, `<th scope="col">`
- All interactive elements keyboard accessible (Tab, Enter, Escape)
- Color never sole information carrier (text labels accompany badges)
- `aria-label` on icon-only buttons
- `aria-live="polite"` on live indicator
- Focus management: filter changes reset focus to first result
- WCAG 2.2 AA color contrast (verified against design tokens)

---

## Motion
- Row hover: `transition-colors duration-150` subtle background shift
- Live indicator: slide-down + fade-in (300ms)
- Filter changes: table refetch with `keepPreviousData` for no-flash pagination
- `prefers-reduced-motion` respected: all animations disabled

---

## Edge Cases
- 0 events total → empty initial state
- 0 results for filter → empty filtered state with clear button
- Very long values → `truncate` + `Tooltip` on hover
- Export > 10,000 rows → toast warning, cap at 10,000
- Concurrent filter changes → abort previous request (React Query `queryKey` invalidation)

---

## Acceptance Criteria
- [ ] Page renders at `/app/iam/activity` under IAM sidebar
- [ ] Table shows events with correct type badges, user info, time, and details
- [ ] Stat pills filter by event category on click
- [ ] Date range, event type, org selects work
- [ ] Search filters by user name/email/IP
- [ ] Pagination works (page + page size)
- [ ] Export CSV downloads correctly
- [ ] Loading state shows skeleton rows
- [ ] Empty states render correctly
- [ ] Error state shows toast with retry
- [ ] Dark mode renders correctly
- [ ] Keyboard navigation works
- [ ] Responsive at tablet and mobile breakpoints

---

## Implementation Notes
- Route: add `{ path: "/app/iam/activity", element: <ActivityTimelinePage /> }` to `router.tsx`
- Sidebar: add menu entry `{ id: "activity", name: "Activity Timeline", path: "/app/iam/activity", icon: Activity }`
- Hook: `useGetActivityTimeline` using `@tanstack/react-query` with `keepPreviousData`
- URL state: all filters synced via `nuqs` for shareable/bookmarkable views
- Estimated effort: 1-2 days frontend