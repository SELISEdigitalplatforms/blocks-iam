# design-card.md — Living Design Card

## Executive Summary

Three design variants for the User Activity Timeline feature ([#155](https://github.com/SELISEdigitalplatforms/blocks-iam/issues/155)). All three extend the existing blocks-iam design system using shadcn/ui components, DM Sans font, and HSL tokens from `globals.css`. **Variant 1 (Conservative Evolution) is recommended** — 100% component reuse, lowest effort, natural extension of existing patterns.

**Status:** Complete

---

## Progress Tracker

- [x] Repository inspection
- [x] Issue analysis
- [x] Pattern discovery
- [x] Design exploration (3 variants)
- [x] Desktop mockups (Light + Dark)
- [x] Tablet mockups (strategy documented)
- [x] Mobile mockups (strategy documented)
- [x] Component mapping
- [x] Comparison matrix
- [x] Recommendation
- [x] Implementation skill (skill.md)

---

## GitHub Issue Summary

**#155 — User Activity Timeline:** Cross-user, dashboard-level chronological view aggregating authentication events, user lifecycle changes, session activity, and permission mutations. Filterable by event type, user, organization, date range, IP, and free-text search. Exportable to CSV.

---

## Repository Analysis

### Existing Components Catalogued (35)

`Accordion` `Alert` `Badge` `Button` `Calendar` `Card` `Checkbox` `Collapsible` `Command` `Dialog` `Drawer` `DropdownMenu` `Form` `HoverCard` `Input` `InputOTP` `Label` `Pagination` `Popover` `Progress` `RadioGroup` `ScrollArea` `Select` `Separator` `Sheet` `Skeleton` `Slider` `Switch` `Table` `Tabs` `Textarea` `Toast` `Tooltip`

**Custom components:** `SearchInput` `Stepper` `PasswordInput` `PageBreadcrumb`

### Design Tokens

| Token | Light | Dark |
|-------|-------|------|
| Font | DM Sans | same |
| Background | hsl(0 0% 100%) | hsl(222.2 84% 4.9%) |
| Card | hsl(0 0% 100%) | hsl(222.2 84% 4.9%) |
| Primary | hsl(206 100% 35%) | hsl(202 100% 43%) |
| Border | hsl(214.3 31.8% 91.4%) | hsl(217.2 32.6% 17.5%) |
| Success | hsl(146 79% 44%) | same |
| Error | hsl(0 100% 60%) | same |
| Radius | 0.5rem | same |

### Similar Pages (Pattern Sources)

| Page | Pattern |
|------|---------|
| `UserHistories` | Card > CardContent > Table(React Table) > Skeleton > Pagination |
| `UserDevices` | Card > CardContent > Table > Pagination, `useState` filter |
| `Users` | Card > CardHeader(FilterToolbar) > CardContent(Table) > Pagination, `nuqs` |
| `IamLogs` | Dashboard-level page, `PageBreadcrumb` |

### UX Conventions

- Cards wrap tables: `<Card><CardContent>...</CardContent></Card>`
- Column headers: `font-bold text-medium-emphasis`
- Loading: `<Skeleton className="h-12 w-full rounded-xl"/>` × 10
- Pagination: `<Pagination page={n} pageSize={n} totalCount={n} pageSizeOptions={[5,10,20,40]} />`
- Filter state: `useState({page, pageSize, filter})` or `nuqs`
- Loading flag: `const loading = isLoading || isFetching`
- Time display: `formatDistanceToNow` from `date-fns`

---

## Variant 1: Conservative Evolution

**Stance:** Maximum consistency. Direct extension of `UserHistories`/`UserDevices`.

### Desktop

| Light | Dark |
|-------|------|
| ![C1L](https://github.com/SELISEdigitalplatforms/blocks-iam/blob/dev/.hermes/specs/sketches/concept-linear-dense/screenshot-light.png?raw=true) | ![C1D](https://github.com/SELISEdigitalplatforms/blocks-iam/blob/dev/.hermes/specs/sketches/concept-linear-dense/screenshot-dark.png?raw=true) |

### Responsive Strategy
- **Tablet:** Filters wrap to 2 rows, table columns condense
- **Mobile:** Table → card list using `<Sheet>` for filters

### Component Mapping (100% reuse)

| Element | Component |
|---------|-----------|
| Card | `<Card>` + `<CardContent>` |
| Table | `<Table>` + `<TableHeader>` + `<TableBody>` |
| Badge | `<Badge>` variant="secondary" |
| Filter buttons | `<Button>` variant="outline" size="sm" |
| Search | `<Input>` |
| Loading | `<Skeleton>` |
| Pagination | `<Pagination>` |
| Breadcrumb | `<PageBreadcrumb>` |

---

## Variant 2: Enhanced Existing

**Stance:** Refined UX with sidebar. Same table pattern, added filter navigation.

### Desktop

| Light | Dark |
|-------|------|
| ![C2L](https://github.com/SELISEdigitalplatforms/blocks-iam/blob/dev/.hermes/specs/sketches/concept-vercel-enterprise/screenshot-light.png?raw=true) | ![C2D](https://github.com/SELISEdigitalplatforms/blocks-iam/blob/dev/.hermes/specs/sketches/concept-vercel-enterprise/screenshot-dark.png?raw=true) |

### Responsive Strategy
- **Desktop:** 260px sticky sidebar + table
- **Tablet:** Sidebar collapses to horizontal filter bar
- **Mobile:** Sidebar → `<Sheet>` bottom drawer, table → card list

### Component Mapping (95% reuse)

| Element | Component |
|---------|-----------|
| Sidebar | `<ScrollArea>` + `<Button>` variant="ghost" |
| Table | Same as Variant 1 |
| All other | Same as Variant 1 |

---

## Variant 3: Modern Premium

**Stance:** Editorial timeline. Prose narratives, maximum whitespace.

### Desktop

| Light | Dark |
|-------|------|
| ![C3L](https://github.com/SELISEdigitalplatforms/blocks-iam/blob/dev/.hermes/specs/sketches/concept-claude-minimal/screenshot-light.png?raw=true) | ![C3D](https://github.com/SELISEdigitalplatforms/blocks-iam/blob/dev/.hermes/specs/sketches/concept-claude-minimal/screenshot-dark.png?raw=true) |

### Responsive Strategy
- **All breakpoints:** Single column timeline, filters wrap
- **Mobile:** Same layout, smaller type, touch-friendly spacing

### Component Mapping (100% reuse)

| Element | Component |
|---------|-----------|
| List container | `<div>` with border-left |
| Filter chips | `<Button>` variant="outline" size="sm" |
| Search | `<Input>` |
| Load more | `<Button>` variant="outline" |
| Metadata tags | `<span>` with monospace + accent bg |

---

## Comparison Matrix

| Dimension | C1: Conservative | C2: Enhanced | C3: Premium |
|-----------|:---:|:---:|:---:|
| Consistency | ★★★★★ | ★★★★ | ★★★ |
| Component reuse | 100% | 95% | 100% |
| Dev effort | Lowest | Medium | Low |
| Data density | Very High | Medium | Low |
| Filter UX | Medium | High | Medium |
| Accessibility | ★★★★★ | ★★★★ | ★★★★ |
| Visual appeal | ★★★ | ★★★★ | ★★★★★ |
| Mobile | Conversion needed | Good | Good |

---

## Recommendation

**Variant 1 (Conservative Evolution)** — ship first.

1. 100% component reuse, zero new components
2. Natural extension of `UserHistories`/`UserDevices`
3. Users already know this interaction model
4. Variant 2 sidebar can be added as enhancement later

---

## Assumptions
- Backend `GET /api/iam/activity/timeline` returns paginated, filtered data
- `@tanstack/react-table`, `nuqs`, `date-fns`, `lucide-react` all available
- Dark mode via `.dark` class on `<html>`

## Open Questions
- [ ] Live updates: SignalR or polling?
- [ ] Export format: CSV only?
- [ ] Permission name?

## Implementation Notes
- Route: `/app/iam/activity`
- Files: 1 route + 2-3 components + 1 hook + navigation config
- Effort: 1-2 days for Variant 1

## Related
- Feature: [#155](https://github.com/SELISEdigitalplatforms/blocks-iam/issues/155)
- Design spec: `.hermes/specs/user-activity-timeline.frontend-design.md`
- AI skill: `.hermes/specs/activity-timeline.skill.md`