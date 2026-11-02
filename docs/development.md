# Development guide

How to run AniTether locally, test it, and find your way around the code.

- [Requirements](#requirements)
- [First-time setup](#first-time-setup)
- [Running it](#running-it)
- [Checking your work](#checking-your-work)
- [Database changes](#database-changes)
- [How the code is organized](#how-the-code-is-organized)
- [Common changes](#common-changes)

## Requirements

- **Docker** (for PostgreSQL and the tests), usable without `sudo`.
- **.NET 10 SDK** and the EF Core tool: `dotnet tool install --global dotnet-ef`.
- **Node.js 20+** and npm.

## First-time setup

```bash
docker compose up -d db                 # PostgreSQL on localhost:5432
dotnet restore && npm --prefix web ci   # dependencies
export ConnectionStrings__Default='Host=localhost;Port=5432;Database=anitether;Username=anitether;Password=anitether'
dotnet ef database update --project api # create the tables
```

There's no signup, so create your first Admin by starting the API once with:

```bash
Bootstrap__AdminEmail=you@example.com Bootstrap__AdminPassword='Change-me-1' dotnet run --project api
```

It creates that account only if no Admin exists yet (an existing account with that email is
promoted instead, keeping its password). Later starts don't need the two variables.

## Running it

Two terminals:

```bash
# 1. API on http://localhost:5000 (needs ConnectionStrings__Default, see above)
dotnet run --project api

# 2. Web app on http://localhost:5173 (forwards /api to the API)
npm --prefix web run dev
```

Open <http://localhost:5173>. The web app reloads on save; the API needs a restart after
C# changes.

**Optional settings** (environment variables for the API):

| Variable | For |
|---|---|
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__From` | Source-health emails to Admins. |
| `HealthChecks__Enabled=false` | Turn off the daily source checks while developing. |

## Checking your work

The check every change must pass:

```bash
dotnet build && dotnet test && npm --prefix web run build
```

- `dotnet test` runs the xUnit tests in `api.Tests/`. Integration tests start a real
  PostgreSQL in Docker (Testcontainers), so Docker must be running. No in-memory database:
  it hides real Postgres behaviour.
- One test class or test while iterating: `dotnet test --filter <Name>`.
- `npm --prefix web run build` type-checks and builds the web app.
- External services (AniList, streaming sites) are faked at the HTTP level in tests
  (`api.Tests/FakeHttpHandler.cs`); nothing in the test suite touches the internet.
- For UI changes, also try them in the browser: type checks don't catch everything.

## Database changes

1. Change the entity in `api/Data/` (and `AppDbContext` if needed).
2. `dotnet ef migrations add <DescriptiveName> --project api`
3. Read the generated file in `api/Migrations/` before applying it.
4. `dotnet ef database update --project api` (with `ConnectionStrings__Default` set).

In production the API connects as a role that can only read and write rows, so a migration
must never rely on creating tables at runtime.

## How the code is organized

```
api/                       ASP.NET Core minimal API (C#)
  Program.cs               startup: services, security, middleware, endpoint registration
  Data/                    entities, AppDbContext, whitelists (Roles, Languages, Themes…)
  Migrations/              EF Core migrations
  Features/<Feature>/      one folder per feature: endpoints + DTOs + logic together
    Auth/                  login, logout, /api/me, preferences, password change
    Users/                 admin user management, roles, first-admin bootstrap
    Anime/                 AniList client, catalog, anime page, link lookup
    List/                  the user's list
    Import/                AniList and MyAnimeList import
    Sources/               streaming sources, link lookup, health checks
    Sync/                  offline sync for the mobile client
api.Tests/                 xUnit tests
web/                       React + TypeScript + Vite
  public/theme.js          applies the theme before the page first paints
  src/api.ts               every API call and response type
  src/App.tsx              routes and the top bar
  src/pages/               one component (+ its CSS) per page or piece
  src/index.css            theme colours and shared styles
docs/                      these guides
deploy/, Dockerfile, docker-compose.prod.yml, scripts/   self-hosting
```

Rules of thumb:

- **Feature folders**, no repository layer over EF Core, no interface with a single
  implementation.
- **Progress rules live in one place**: `ListEntry.ApplyProgress`/`SetProgress`. Every
  path that changes progress goes through them.
- **The server never touches video.** It stores links and progress; the browser fetches
  everything from the external site.
- **Validate at the edges**: every URL stored must be `https://` on an expected host.
- **Plain CSS**, colours only through the theme variables in `index.css`.

## Common changes

| Change | Where |
|---|---|
| Add a streaming site | No code: the admin Sources page (a URL-template row). Live-check the site first. |
| New setting / environment variable | Read it via `IConfiguration`; document it in `deploy/env.example` and `docker-compose.prod.yml`. |
| New theme colour | `index.css` (every palette block sets every colour), `api/Data/Themes.cs`, `web/public/theme.js`, `web/src/theme.ts` — keep the lists in sync. |
| New catalog filter | `web/src/catalogFilters.ts` and `api/Features/Anime/CatalogFilter.cs` together. |
| New admin-only endpoint | `.RequireAuthorization(p => p.RequireRole(Roles.Admin))` and a rate limit; test that a non-admin gets 403. |
