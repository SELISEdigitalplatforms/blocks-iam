## Variant: Table-Dense

### Design stance
Dense, utilitarian, data-first. Power-user oriented. The closest to the existing IAM design patterns (UserHistories, UserDevices).

### Key choices
- Layout: Full-width card with table, horizontal stat pills, inline filter bar
- Typography: Compact — 13px body, 12px column headers
- Color: Dark theme, subtle borders, color-coded event badges
- Interaction: Quick stat pills filter on click, live indicator bar, standard pagination

### Trade-offs
- Strong at: Scanning large volumes quickly, keyboard navigation, data density
- Weak at: Visual storytelling, casual users, mobile (requires full table → card conversion)

### Best for
- Admin operators monitoring 1,500+ events/day
- Teams that need Excel-like scanning speed
- Fits existing IAM codebase patterns with minimal new patterns