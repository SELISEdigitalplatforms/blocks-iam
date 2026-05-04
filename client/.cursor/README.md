## Client Cursor workspace

### Canonical architecture (repo)

**Primary documentation:** [`../architect/`](../architect/) — `README.md`, `ARCHITECTURE.md`, `SYSTEM_MAP.md`, `MODULES.md`, `GRAPH_OVERVIEW.md`.

Update those files when the app structure or API strategy changes; then sync summaries under `context/` if needed.

### This folder (`.cursor/`)

| Path | Role |
|------|------|
| `rules/` | Cursor rules (`*.mdc`): graphify, architecture, debounce/search, **git branch & commit**. |
| `context/architecture/` | Short entry + **links** to `client/architect/*` (avoid long drift from canonical). |
| `context/graphify/` | Quick graph summary; align with `client/graphify-out/` and `architect/GRAPH_OVERVIEW.md`. |

### Data sources

- **Graphify artifacts:** `client/graphify-out/` (gitignored). Regenerate after large refactors.
- **Stack / routes / modules:** `client/architect/`.
