# BlocksTemplate

ASP.NET Core 10 + React (Vite, TypeScript) starter. The **API and the built SPA share one origin**: `npm run build` writes static files to `server/Api/wwwroot`; Kestrel serves those assets and `/api/*`. Use **`./run.sh`** from the repo root to build the client then start the API, or run `npm run build` in `client/` before **`dotnet run`**—Node/npm are used at **build** time only, not a Vite dev proxy in production.

## Project structure

```
blocks-template/
├── client/                      # React + Vite + TypeScript (source only at runtime)
│   ├── app/                     # Application code (main.tsx, router, pages, components, api)
│   ├── public/                  # Static assets copied as-is by Vite
│   ├── index.html
│   ├── vite.config.ts           # build.outDir → ../server/Api/wwwroot
│   ├── package.json
│   └── .env.example             # Copy to .env for BLOCKS_X_BLOCKS_KEY (see below)
├── server/
│   ├── Api/                     # Web host (Kestrel, Genesis, controllers)
│   │   ├── Controllers/         # API controllers ([Route] uses /api prefix via convention)
│   │   ├── wwwroot/             # Client app output from Vite (generated; do not edit by hand)
│   │   ├── Program.cs
│   │   ├── Api.csproj           # Web host project (no MSBuild npm hook; build client separately)
│   │   └── GlobalApiRoutePrefixConvention.cs
│   ├── DomainService/           # Domain logic, models, validators, entities
│   ├── Worker/                  # worker project to run background tasks
│   └── Blocks.slnx              # Solution (Api, DomainService, Worker)
├── run.sh                       # Build client then run Api (see script)
├── LICENSE
└── README.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js LTS](https://nodejs.org/) (for `npm install` / `npm run build` only)

## How to run (development)

From the repository root (builds the client, then starts the API):

```bash
./run.sh
```

If you already built the client (`npm run build` in `client/`), you can run only the host:

```bash
dotnet run --project server/Api/Api.csproj
```

Or from the Api folder:

```bash
cd server/Api
dotnet run
```

Open the URL from **`launchSettings.json`** (default: **`http://localhost:5000`**). The same host serves the React app from **`wwwroot`** and **`/api/*`**.

### Client environment (Genesis)

API calls expect an **`x-blocks-key`** header when your Blocks/Genesis setup requires it. At **build** time, Vite inlines values from `client/.env`:

1. Copy **`client/.env.example`** → **`client/.env`**.
2. Set **`BLOCKS_X_BLOCKS_KEY`** to match your environment.
3. Run **`npm run build`** in **`client/`** (or **`./run.sh`**) so the client bundle is rebuilt.

### Optional: Vite dev server (UI only)

If you want hot reload for React without rebuilding the full Api project:

```bash
cd client && npm run dev
```

That serves the UI on Vite’s port only; the API stays on Kestrel (different origin), so you would configure CORS or another gateway if you need both during UI work. For an integrated app, build the client into **`wwwroot`** then use **`dotnet run`** (or **`./run.sh`**).

## Production / publish

Build the client, then publish the API ( **`wwwroot`** must exist before publish if you want the SPA in the output):

```bash
(cd client && npm run build)
dotnet publish server/Api/Api.csproj -c Release -o ./publish
```

You do not need a separate Node process on the server at runtime.

For local development with the same two steps in one command:

```bash
./run.sh
```

(`run.sh` runs a **Debug** `dotnet run`; use the commands above for **Release** publish.)

## API

- **`/api/events`** — Events CRUD, pagination, `search` (name/location/organizer, ≥3 characters), date range overlap, sorting (FluentValidation on create/update).

Controller route templates omit the **`api`** segment in code; **`GlobalApiRoutePrefixConvention`** in **`Program.cs`** prefixes **`api`** for all attribute-routed controllers.

## Frontend routing

**`/api` is reserved for the HTTP API.** The React router redirects mistaken navigations to `/api` back home and, in development, **`assertFePathNotApi`** helps catch invalid route paths at startup.

## License

See [LICENSE](LICENSE).
