## Variant: Split Hybrid

### Design stance
Information architecture-first. Left sidebar contains all filters and quick stats; right content area shows the event stream. Date-grouped rows with compact layout. Balances filterability with scan-ability.

### Key choices
- Layout: 300px persistent sidebar + fluid content area, date-divided event rows
- Typography: Compact rows — 14px titles, badge labels, monospace IPs
- Color: Dark theme, sidebar with chip groups, color-coded type badges
- Interaction: Sidebar chips toggle filters, date dividers for visual grouping, dot pagination

### Trade-offs
- Strong at: Persistent filter visibility, quick type switching, grouped time navigation
- Weak at: Mobile (sidebar collapses), requires more screen width
- Best for: Teams that frequently switch between event types and time ranges

### Best for
- Operators who constantly change filters
- Multi-tenant environments where org-switching is common
- Desktop-first workflows