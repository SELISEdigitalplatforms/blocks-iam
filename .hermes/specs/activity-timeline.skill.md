# skill.md — AI Implementation Guide

## Project Context

**Feature:** User Activity Timeline
**Issue:** [#155](https://github.com/SELISEdigitalplatforms/blocks-iam/issues/155)
**Design Card:** `.hermes/specs/design-card.md`
**Design Spec:** `.hermes/specs/design.md`

### What to build
A dashboard-level page at `/app/iam/activity` showing all user activity events in a filterable, paginated table. Aggregates data from three existing backend endpoints.

### Related pages (study these first)
- `client/app/idp/iam/modules/user-management/user-histories/user-histories.tsx`
- `client/app/idp/iam/modules/user-management/user-devices/user-devices.tsx`
- `client/app/idp/iam/modules/user-management/users/users.tsx`

### Existing patterns (MUST follow)
- Card > CardContent > Table > Pagination
- `useState` filter: `{page: 0, pageSize: 10, filter: {...}}`
- `const loading = isLoading || isFetching`
- `<Skeleton className="h-12 w-full rounded-xl"/>` × 10 for loading
- `<Pagination page={n} pageSize={n} totalCount={n} onChange={fn} onPageSizeChange={fn} pageSizeOptions={[5,10,20,40]} />`
- `<span className="font-bold text-medium-emphasis">` for column headers
- `formatDistanceToNow` + absolute timestamp for time display
- `nuqs` for URL-synced filter state

---

## Design Principles (always apply)

1. **Extend, never redesign.** This feature must look like it has always existed in the product.
2. **Reuse before creating.** Before writing any new component, search the repo for an existing equivalent.
3. **Match existing patterns exactly.** Study `UserHistories` and `UserDevices` — your code should look like it was written by the same team.
4. **Compose from primitives.** Build new UI by combining existing shadcn/ui components; only create new components as a last resort.

---

## Component Rules

### ✅ Always use
- `@/components/ui-kits/card/card` → `Card`, `CardContent`
- `@/components/ui-kits/table/table` → `Table`, `TableHeader`, `TableBody`, `TableRow`, `TableHead`, `TableCell`
- `@/components/ui-kits/badge/badge` → `Badge`
- `@/components/ui-kits/button/button` → `Button`
- `@/components/ui-kits/input/input` → `Input`
- `@/components/ui-kits/select/select` → `Select`
- `@/components/ui-kits/skeleton/skeleton` → `Skeleton`
- `@/components/ui-kits/pagination/pagination` → `Pagination`
- `@/components/ui-kits/dialog/dialog` → `Dialog` (export modal)
- `@/components/ui-kits/tooltip/tooltip` → `Tooltip`
- `@seliseblocks/blocks-kit` → `PageBreadcrumb`

### ❌ Never use
- Raw HTML tables (always shadcn/ui `Table`)
- Inline styles with hardcoded colors (always Tailwind + tokens)
- Any component library not already in the project
- Radix primitives directly (use the shadcn/ui wrapper)

---

## Icon Rules

### ✅ Only use lucide-react
- `Search` for search input icon
- `Activity` for sidebar menu item
- `Download` for export button
- `Filter`, `X`, `RefreshCw` for filter controls
- `Key`, `Shield`, `UserPlus`, `LogOut`, `UserCog`, `ShieldCheck` for event type icons

### ❌ Never import from
- `heroicons`
- `@fortawesome`
- `@mui/icons-material`
- `react-icons`
- `@phosphor-icons`

---

## Styling Rules

### ✅ Always
- Use Tailwind utility classes
- Use CSS variables from `:root` / `:root[class~="dark"]`
- Use `text-high-emphasis`, `text-medium-emphasis`, `text-low-emphasis`
- Use `bg-card`, `bg-background`, `border-border`
- Use semantic colors: `text-success`, `text-error`, `bg-primary`
- Respect radius: `rounded-lg` (= `var(--radius)`)
- Respect spacing: `gap-6`, `px-4 pt-4 md:px-6 md:pt-6`

### ❌ Never
- Hardcode hex colors or hsl() values
- Hardcode font sizes (use Tailwind scale: `text-xs` through `text-2xl`)
- Hardcode shadows or border-radius
- Use inline styles except for truly dynamic values

---

## Responsive Rules

Every implementation must support three breakpoints:

### Desktop (≥1024px)
Full layout with table, inline filters, standard pagination.

### Tablet (640px–1023px)
- Filters wrap/stretch to fill width
- Table columns condense
- Export button becomes icon-only

### Mobile (<640px)
- Table → card stack (each row becomes a `Card`)
- Filters → `<Sheet>` bottom drawer with floating trigger button
- Pagination → "Load more" button
- Touch targets ≥44px (Tailwind: `min-h-[44px]`)

Use Tailwind responsive prefixes: `md:`, `lg:`.

---

## Accessibility Rules

- `<th scope="col">` on all column headers
- `<thead>`, `<tbody>` in tables
- `aria-label` on icon-only buttons
- `aria-live="polite"` on live indicator
- Keyboard: Tab through filters, Enter to activate, Escape to close dialogs
- Focus ring visible on all interactive elements
- Color never sole information carrier (text labels always accompany badges)
- `prefers-reduced-motion` respected

---

## Pattern Rules (MANDATORY)

Before creating anything new:

1. **Search** the repository for similar pages
2. **Study** `UserHistories`, `UserDevices`, `Users` components
3. **Copy** their structure: Card > Content > Table/List > Pagination
4. **Reuse** their hooks pattern: `useState` filter + React Query
5. **Reuse** their loading pattern: Skeleton × 10
6. **Reuse** their pagination component with same props
7. **Match** their file structure: `modules/user-management/activity-timeline/`

---

## AI Decision Priority

When making implementation decisions:

1. **Existing application pages** (copy from `UserHistories`/`UserDevices`)
2. **Existing reusable components** (shadcn/ui)
3. **Existing design system** (Tailwind tokens, globals.css)
4. **Existing UX patterns** (Card > Table > Pagination)
5. **Modern UX** (WCAG, responsive)
6. **External inspiration** (Claude, Linear, etc. — only if nothing above works)

---

## Component Mapping

| UI Element | Component | Path |
|-----------|-----------|------|
| Page wrapper | `<Card>` + `<CardContent>` | `@/components/ui-kits/card/card` |
| Data table | `<Table>` family | `@/components/ui-kits/table/table` |
| Event type label | `<Badge>` variant="secondary" | `@/components/ui-kits/badge/badge` |
| Filter pills | `<Button>` variant="outline" size="sm" | `@/components/ui-kits/button/button` |
| Search | `<Input>` + lucide `Search` icon | `@/components/ui-kits/input/input` |
| Loading | `<Skeleton>` | `@/components/ui-kits/skeleton/skeleton` |
| Pagination | `<Pagination>` | `@/components/ui-kits/pagination/pagination` |
| Export modal | `<Dialog>` | `@/components/ui-kits/dialog/dialog` |
| Breadcrumb | `<PageBreadcrumb>` | `@seliseblocks/blocks-kit` |

**New components: None.** All UI composed from existing primitives.

---

## Implementation Checklist

Before declaring done:

- [ ] Existing components reused (no new components created)
- [ ] shadcn/ui only (no other component library)
- [ ] lucide-react only (no other icon library)
- [ ] Tailwind CSS only (no inline styles with hardcoded values)
- [ ] Design tokens from globals.css used (no hardcoded colors)
- [ ] Desktop verified (≥1024px renders correctly)
- [ ] Tablet verified (640–1023px renders correctly)
- [ ] Mobile verified (<640px renders correctly)
- [ ] Light Mode verified
- [ ] Dark Mode verified
- [ ] Loading state shows skeletons
- [ ] Empty state renders
- [ ] Error state shows toast
- [ ] Keyboard navigation works
- [ ] Screen reader accessible
- [ ] No new npm packages added
- [ ] No new fonts added
- [ ] Pattern matches `UserHistories`/`UserDevices` structure