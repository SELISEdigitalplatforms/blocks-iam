# User Activity Timeline — Frontend Design

## Design System Alignment

Fully coherent with the existing blocks-iam design language:

| Element | Source |
|---|---|
| **Color tokens** | `@seliseblocks/blocks-kit` CSS variables (`--blocks-primary-*`, `--high-emphasis`, `--medium-emphasis`, `--success`, `--destructive`) |
| **Typography** | Inter (system), `text-2xl font-bold tracking-tight` headings, `text-sm` body, `font-bold text-medium-emphasis` column headers |
| **Components** | `Card`/`CardContent`, `Tabs`, `Table`/`TableHeader`/`TableRow`, `Select`, `Pagination`, `Skeleton`, `Badge` |
| **Icons** | `lucide-react` (`Activity`, `Download`, `Filter`, `Search`, `X`, `ChevronDown`) |
| **Spacing** | `px-4 pt-4 md:px-6 md:pt-6` page wrapper, `gap-6` card stacks |
| **Patterns** | Matches existing `UserHistories` / `UserDevices` — Card → Table → Pagination |

---

## Page: Activity Timeline (`/app/iam/activity`)

### Full Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│  IAM  >  Activity Timeline                                               │
│                                                                          │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  ⏱ Activity Timeline                               [📥 Export CSV] │ │
│  │                                                                    │ │
│  │  ┌──────────────────────────────────────────────────────────────┐ │ │
│  │  │ 🔍 Search users or events...     │ 📅 Date range ▼ │ 🏷 Type ▼│ │ │
│  │  │                                  │ 🏢 Organization ▼ │ 🔄     │ │ │
│  │  └──────────────────────────────────────────────────────────────┘ │ │
│  │                                                                    │ │
│  │  ┌─ Live indicator (new events streaming) ──────────────────────┐ │ │
│  │  │ ● 3 new events · Click to refresh                            │ │ │
│  │  └──────────────────────────────────────────────────────────────┘ │ │
│  │                                                                    │ │
│  │  ┌──────────────────────────────────────────────────────────────┐ │ │
│  │  │  ■  ■  ■  □  □  □  □   Quick stats: last 7 days             │ │ │
│  │  │  42  15  8    Logins  MFA uses  Failures                     │ │ │
│  │  └──────────────────────────────────────────────────────────────┘ │ │
│  │                                                                    │ │
│  │  ╔════╤═══════════╤══════════════════╤══════════════╤════════════╗ │ │
│  │  ║    │ User      │ Event            │ When         │ Details    ║ │ │
│  │  ╠════╪═══════════╪══════════════════╪══════════════╪════════════╣ │ │
│  │  ║ 🔑 │ Jane Doe  │ Login via        │ 2 minutes    │ 10.0.0.1   ║ │ │
│  │  ║    │ jane@s.com│ Password         │ ago          │ Chrome/Win ║ │ │
│  │  ╟────┼───────────┼──────────────────┼──────────────┼────────────╢ │ │
│  │  ║ 🛡 │ Bob Smith │ MFA Enabled      │ 12 minutes   │ 10.0.0.42  ║ │ │
│  │  ║    │ bob@s.com │                  │ ago          │ Chrome/Mac ║ │ │
│  │  ╟────┼───────────┼──────────────────┼──────────────┼────────────╢ │ │
│  │  ║ 👤 │ Admin     │ User Created     │ 1 hour ago   │ —          ║ │ │
│  │  ║    │ admin@s.. │ alice@s.com      │              │            ║ │ │
│  │  ╟────┼───────────┼──────────────────┼──────────────┼────────────╢ │ │
│  │  ║ ⛔ │ Carol L.  │ Account          │ 2 hours ago  │ —          ║ │ │
│  │  ║    │ carol@s.. │ Deactivated      │              │            ║ │ │
│  │  ╟────┼───────────┼──────────────────┼──────────────┼────────────╢ │ │
│  │  ║ 🔄 │ Dave K.   │ Token Renewed    │ 3 hours ago  │ 10.0.0.88  ║ │ │
│  │  ║    │ dave@s.com│                  │              │ Safari/iOS ║ │ │
│  │  ╚════╧═══════════╧══════════════════╧══════════════╧════════════╝ │ │
│  │                                                                    │ │
│  │  Showing 1-20 of 1,542              ◀ 1  2  3 ... 78 ▶           │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Component Breakdown

### 1. Page Shell

```
ActivityTimelinePage
├── PageBreadcrumb              ← "IAM > Activity Timeline"
├── ActivityHeader              ← Title + Export button
├── ActivityQuickStats          ← 7-day stat pills
├── ActivityFilterBar           ← Search + date + type + org filters
├── ActivityLiveIndicator       ← Real-time event count badge
├── ActivityTable               ← Main data table
└── Pagination                  ← Page controls
```

### 2. Quick Stats Bar

```tsx
// Horizontal stat pills — clickable to filter
const stats = [
  { label: "Logins", count: 42, icon: Key, color: "blue", filter: "login_via_*" },
  { label: "MFA Events", count: 15, icon: Shield, color: "amber", filter: "login_via_mfa_code,user_mfa_*" },
  { label: "Failures", count: 8, icon: AlertTriangle, color: "red", filter: "*revoked,*logout_all" },
  { label: "User Changes", count: 12, icon: Users, color: "green", filter: "user_*" },
]
```

```
┌──────────────────────────────────────────────────────────────────────┐
│  [Key] 42 Logins    [Shield] 15 MFA    [AlertTriangle] 8 Failures   │
│         this week            this week            this week          │
└──────────────────────────────────────────────────────────────────────┘
```

Each pill is a `<button>` that sets the corresponding event type filter on click. Active pill gets a filled variant, others are outline.

### 3. Filter Bar

```
┌────────────────────────────────────────────────────────────────────────┐
│ ┌──────────────────────┐ ┌─────────────┐ ┌──────────────────┐  [🔄] │
│ │ 🔍 Search...          │ │ 📅 Last 7d ▼│ │ 🏷 All Events ▼ │       │
│ └──────────────────────┘ └─────────────┘ └──────────────────┘        │
│                         ┌──────────────┐                               │
│                         │ 🏢 All Orgs ▼│                               │
│                         └──────────────┘                               │
└────────────────────────────────────────────────────────────────────────┘
```

**Search:** Debounced (300ms) free-text across userName, email, IP. Uses `cmdk` Command component for autocomplete suggestions.

**Date Range:** Presets: Last hour, Today, Last 7 days, Last 30 days, Custom range (opens date picker with `react-day-picker` already in deps).

**Event Type:** Multi-select dropdown. Groups:
- Authentication: login via password, login via social, login via SSO, login via auth code
- Security: MFA enabled, MFA disabled, MFA code login, session revoked, logout all
- User Lifecycle: user created, user updated, user activated, user deactivated, email verified
- Tokens: token renewed, refresh token issued, refresh token revoked
- Permissions: roles changed, permissions changed

**Organization:** Single-select from user's orgs. "All Orgs" default.

**Reset:** The 🔄 button clears all filters.

### 4. Live Indicator

```tsx
// Shows when new events arrive since last fetch
// Uses a pulsing green dot + count
```

```
┌──────────────────────────────────────────────┐
│  ● 3 new events · Click to refresh           │
└──────────────────────────────────────────────┘
```

Appears as a floating bar below the filter bar. Animated slide-down. Click triggers `queryClient.invalidateQueries(["activity-timeline"])`. Auto-dismisses after click. Polls every 30s (or uses SignalR if available).

### 5. Event Table — Detailed Row Design

```
╔════╤════════════════════╤══════════════════════════╤══════════════════╤══════════════════════╗
║ #  │ User               │ Event                     │ When             │ Details              ║
╠════╪════════════════════╪══════════════════════════╪══════════════════╪══════════════════════╣
║    │                    │                           │                  │                      ║
║ 🔑 │ ┌────────────┐     │ ┌──────────────────────┐ │ 2 minutes ago    │ ┌──────────────────┐ ║
║    │ │ JD         │     │ │ Login via Password   │ │                  │ │ IP: 10.0.0.1     │ ║
║    │ │ Jane Doe   │     │ │                      │ │ Jun 24, 2026     │ │                  │ ║
║    │ │ jane@s.com │     │ └──────────────────────┘ │ 14:32 UTC        │ │ Chrome · Windows │ ║
║    │ └────────────┘     │                           │                  │ │                  │ ║
║    │                    │                           │                  │ └──────────────────┘ ║
║    │                    │                           │                  │                      ║
╟────┼────────────────────┼───────────────────────────┼──────────────────┼──────────────────────╢
║    │                    │                           │                  │                      ║
║ 👤 │ ┌────────────┐     │ ┌──────────────────────┐ │ 1 hour ago       │ ┌──────────────────┐ ║
║    │ │ AS         │     │ │ User Created         │ │                  │ │ Created:         │ ║
║    │ │ Admin      │     │ │                      │ │ Jun 24, 2026     │ │ alice@selise.com │ ║
║    │ │ admin@s... │     │ │ Target: alice@s.com  │ │ 13:15 UTC        │ │                  │ ║
║    │ └────────────┘     │ └──────────────────────┘ │                  │ │ Action by: Admin │ ║
║    │                    │                           │                  │ └──────────────────┘ ║
║    │                    │                           │                  │                      ║
╚════╧════════════════════╧══════════════════════════╧══════════════════╧══════════════════════╝
```

#### Column Specifications

| Column | Width | Content |
|---|---|---|
| **Type icon** | 48px | Color-coded event type icon (see below) |
| **User** | 220px | Avatar initial + name + email. Clickable → user detail |
| **Event** | 240px | Event name (human readable) + optional subtext (target user for admin actions) |
| **When** | 160px | Relative time ("2 min ago") + absolute timestamp below |
| **Details** | flex | IP pill + device info. Variant depending on event type |

#### Event Type Visual System

```
Color  Icon             Event Types
─────  ────             ───────────
🔵 Blue    Key           login_via_password, login_via_social, login_via_sso_consent,
                        login_via_authorization_code
🟣 Purple  ShieldCheck   login_via_mfa_code, user_mfa_enabled, user_mfa_disabled
🟠 Orange  RefreshCw     token_renewed, refresh_token_issued, refresh_token_revoked
🔴 Red     LogOut        session_revoked, logout, logout_all, user_deactivated
🟢 Green   UserPlus      user_created
🟢 Green   UserCheck     user_activated, user_email_verified
🟡 Amber   UserCog       user_updated
🔵 Indigo  Shield         user_roles_changed, user_permissions_changed
```

#### Row States

| State | Visual |
|---|---|
| **Default** | Normal row |
| **Hover** | Slight bg shift, cursor pointer |
| **Click** | Expands inline detail panel (see below) |
| **New** (appeared since last view) | Subtle blue left border that fades after 3s |

### 6. Expandable Row Detail

Clicking a row expands an inline detail panel:

```
╔════╤═══════╤═════════════╤════════╤══════════════════════════════════╗
║ 🔑 │ JD    │ Login via   │ 2m ago │ IP: 10.0.0.1 · Chrome · Windows ║
║    │ Jane  │ Password    │        │                                  ║
╠════╧═══════╧═════════════╧════════╧══════════════════════════════════╣
║ ┌──────────────────────────────────────────────────────────────────┐ ║
║ │ DETAILS                                           [Copy Event ID] │ ║
║ │                                                                  │ ║
║ │ Event ID      evt_a1b2c3d4                                      │ ║
║ │ User ID       user_456                    → View User Profile    │ ║
║ │ Organization  Acme Corp (org_789)          → View Org            │ ║
║ │ IP Address    10.0.0.1                                           │ ║
║ │ User Agent    Mozilla/5.0 ... Chrome/126  Windows 10             │ ║
║ │ Session ID    sess_xyz789                                         │ ║
║ │                                                                  │ ║
║ │ Raw Event Data                          [Expand ▼]               │ ║
║ └──────────────────────────────────────────────────────────────────┘ ║
╚═══════════════════════════════════════════════════════════════════════╝
```

### 7. Empty States

**No events (initial):**

```
┌──────────────────────────────────────────┐
│                                          │
│           [Activity icon — 64px]         │
│                                          │
│        No activity recorded yet          │
│     Activity will appear here as users   │
│     interact with the system.            │
│                                          │
└──────────────────────────────────────────┘
```

**No results (filters applied):**

```
┌──────────────────────────────────────────┐
│                                          │
│           [Search icon — 64px]           │
│                                          │
│    No events matching your filters       │
│                                          │
│        [Clear all filters]               │
│                                          │
└──────────────────────────────────────────┘
```

### 8. Loading State

```
┌────┬────────────────────┬──────────────────┬──────────────┬──────────────┐
│ ▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓ │
│    │ ▓▓▓▓▓▓▓▓▓▓▓▓▓      │                  │ ▓▓▓▓▓▓▓▓▓    │              │
├────┼────────────────────┼──────────────────┼──────────────┼──────────────┤
│ ▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓ │
│    │ ▓▓▓▓▓▓▓▓▓▓▓▓▓      │                  │ ▓▓▓▓▓▓▓▓▓    │              │
├────┼────────────────────┼──────────────────┼──────────────┼──────────────┤
│ ▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓ │ ▓▓▓▓▓▓▓▓▓▓▓ │
│    │ ▓▓▓▓▓▓▓▓▓▓▓▓▓      │                  │ ▓▓▓▓▓▓▓▓▓    │              │
└────┴────────────────────┴──────────────────┴──────────────┴──────────────┘
```

Pulsing skeleton using existing `Skeleton` component. 10 rows during initial load, 5 rows during pagination.

### 9. Export Flow

Clicking "Export CSV" triggers:

```
┌──────────────────────────────────────────┐
│         Export Activity Timeline          │
│                                          │
│  Exporting with current filters:         │
│  • Date: Last 7 days                     │
│  • Type: All events                      │
│  • Org: All organizations                │
│  • Estimated rows: ~1,542                │
│                                          │
│  Format: CSV                             │
│  Max rows: 10,000                        │
│                                          │
│         [Cancel]    [Export CSV]         │
└──────────────────────────────────────────┘
```

After clicking Export:

```
┌──────────────────────────────────────────┐
│                                          │
│            [Download icon]               │
│                                          │
│       Preparing your export...           │
│       ┌─────────────────────┐            │
│       │ ████████████░░░░░░░ │ 65%        │
│       └─────────────────────┘            │
│                                          │
└──────────────────────────────────────────┘
```

On success: auto-download + toast "Exported 1,542 events".
On 10k+ limit: toast "Export limited to 10,000 rows. Narrow your filters."

### 10. Pagination

Matches existing `UserDevices` / `UserHistories` pattern:

```
Showing 1-20 of 1,542        [◀] [1] [2] [3] ... [78] [▶]   20 per page ▼
```

---

## Responsive Behavior

### Desktop (≥768px) — Full layout as shown above

### Tablet (640-768px)

- Quick stats wrap to 2 rows
- Table condenses: Event + When column merge, Details truncates
- Filter bar stacks vertically
- Export becomes icon-only button

### Mobile (<640px)

- Quick stats become horizontal scrollable chips
- Table becomes card list:

```
┌─────────────────────────────┐
│ 🔑  Login via Password      │
│     Jane Doe · jane@s.com   │
│     2 minutes ago           │
│     10.0.0.1 · Chrome/Win   │
├─────────────────────────────┤
│ 👤  User Created            │
│     Admin · admin@s.com     │
│     1 hour ago              │
│     Created: alice@s.com    │
├─────────────────────────────┤
│ 🛡  MFA Enabled             │
│     Bob Smith · bob@s.com   │
│     12 minutes ago          │
│     10.0.0.42 · Chrome/Mac  │
└─────────────────────────────┘
```

- Filter bar becomes a slide-up sheet (bottom drawer)
- Floating "Filters (3)" pill button
- Pagination becomes "Load more" button

---

## Interactions & Micro-animations

| Interaction | Animation |
|---|---|
| Row hover | `transition-colors duration-150`, subtle bg shift |
| Row click (expand) | `animate-collapsible-down` (200ms ease-out) |
| Live indicator appear | Slide down + fade in (300ms) |
| Filter change | Table fade out → skeleton → fade in (optimistic) |
| Stat pill click | Scale pulse → color fill transition |
| Export modal | `animate-in fade-in zoom-in-95` (Radix Dialog) |
| Clear filters | All filter values wipe with staggered 50ms fade |

---

## URL State (nuqs)

All filters synced to URL search params for shareable/bookmarkable views:

```
/app/iam/activity?search=jane&dateFrom=2026-06-17&dateTo=2026-06-24&eventTypes=login_via_password,user_created&org=org_789&page=2&pageSize=20
```

Uses `nuqs` (already in package.json) for type-safe URL state management.

---

## Component Tree

```tsx
// Route file: client/app/routes/dashboard/iam-activity.tsx
export default function IamActivityPage() {
  return <ActivityTimeline />;
}

// Main component: client/app/idp/iam/modules/user-management/activity-timeline/
<ActivityTimeline>
  <PageBreadcrumb breadcrumbIndex={2} />
  <ActivityHeader>
    <h3>Activity Timeline</h3>
    <ExportButton onClick={handleExport} />
  </ActivityHeader>

  <ActivityQuickStats
    stats={computedStats}
    onStatClick={handleStatFilter}
    activeFilter={currentEventTypes}
  />

  <ActivityFilterBar>
    <SearchInput value={search} onChange={setSearch} />
    <DateRangeSelect value={dateRange} onChange={setDateRange} />
    <EventTypeMultiSelect value={eventTypes} onChange={setEventTypes} />
    <OrganizationSelect value={org} onChange={setOrg} />
    <ResetFiltersButton onClick={handleReset} />
  </ActivityFilterBar>

  <ActivityLiveIndicator
    newCount={newEventCount}
    onRefresh={handleRefresh}
  />

  <Card>
    <CardContent>
      <ActivityTable
        data={events}
        isLoading={isLoading}
        expandedRow={expandedRow}
        onRowClick={setExpandedRow}
      />
      <Pagination
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        onChange={setPage}
        onPageSizeChange={setPageSize}
      />
    </CardContent>
  </Card>

  <ExportDialog open={showExport} onClose={setShowExport} />
</ActivityTimeline>
```

---

## Tailwind Theme Tokens Used

```css
/* Event type badge backgrounds (matching existing design tokens) */
.badge-login        { @apply bg-blue-500/10 text-blue-500; }
.badge-mfa          { @apply bg-purple-500/10 text-purple-500; }
.badge-token        { @apply bg-orange-500/10 text-orange-500; }
.badge-security     { @apply bg-red-500/10 text-red-500; }
.badge-lifecycle    { @apply bg-green-500/10 text-green-500; }
.badge-permissions  { @apply bg-indigo-500/10 text-indigo-500; }

/* Card */
.activity-card      { @apply bg-card text-card-foreground rounded-xl border shadow-sm; }

/* Stat pills */
.stat-pill          { @apply flex items-center gap-2 px-3 py-2 rounded-lg border text-sm 
                             transition-all duration-200 cursor-pointer hover:border-primary/50; }
.stat-pill-active   { @apply border-primary bg-primary/5 text-primary; }

/* IP address pill */
.ip-pill            { @apply inline-flex items-center px-2 py-0.5 rounded-md 
                             bg-secondary text-secondary-foreground text-xs font-mono; }

/* Live indicator */
.live-indicator     { @apply flex items-center gap-2 px-4 py-2 rounded-lg 
                             bg-primary/5 border border-primary/20 text-sm animate-slide-down; }
.live-dot           { @apply w-2 h-2 rounded-full bg-green-500 animate-pulse; }
```

---

## Files to Create

```
client/app/idp/iam/modules/user-management/activity-timeline/
├── activity-timeline.tsx            # Main page component
├── activity-header.tsx              # Title + export button
├── activity-quick-stats.tsx         # Stat pills bar
├── activity-filter-bar.tsx          # Search + date + type + org + reset
├── activity-live-indicator.tsx      # New events notification
├── activity-table.tsx              # Main data table
├── activity-table-row.tsx          # Single row with expand
├── activity-row-detail.tsx         # Expanded detail panel
├── activity-event-badge.tsx        # Color-coded icon + label
├── activity-export-dialog.tsx      # Export confirmation + progress
├── activity-empty-state.tsx        # Empty/No results
├── activity-mobile-card.tsx        # Mobile card variant
└── index.ts                        # Barrel export

client/app/idp/iam/hooks/
└── use-activity-timeline.ts        # React Query hook

client/app/idp/iam/models/
└── activity.ts                     # Types: ActivityEvent, ActivityFilter, etc.

client/app/idp/iam/constants/
└── activity-constants.ts           # Event labels, icons, colors, quick stat configs

client/app/routes/dashboard/
└── iam-activity.tsx                # Route file (thin wrapper)
```

---

## Navigation Entry

Add to IAM dashboard sidebar:

```typescript
// In navigation configuration
{
  id: "activity-timeline",
  type: "menu",
  name: "Activity Timeline",
  path: "/app/iam/activity",
  icon: Activity,  // lucide-react
}
```

---

## Browser Support

- Chrome 90+, Firefox 90+, Safari 15+, Edge 90+
- Graceful degradation on older browsers (no animations, basic table)
- All interactive elements keyboard-accessible (Tab, Enter, Escape, Arrow keys on date picker)
- Screen reader friendly: ARIA labels on icon-only buttons, `role="row"` on table rows, `aria-expanded` on expandable rows, `aria-live="polite"` on live indicator