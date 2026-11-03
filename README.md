# AniTether

**Track the anime you watch. Open it on a site you choose.** AniTether is a self-hosted anime list and catalog with episode progress, imports, and optional links to your own streaming sources.

[Open the live example](https://froymain.tail144dc9.ts.net/)

> AniTether starts with no streaming sources and includes no video. An administrator adds URL templates in the admin UI: a site name, base URL, and search, episode, or embed patterns. AniTether uses those templates to build ▶ links and an embedded watch page. Like an emulator without bundled games, AniTether is a content-free tool: add only sites and material you have the right to use.
>
> AniTether stores your list, progress, and links. It does not fetch, host, proxy, or re-stream video; playback comes from the site you open.

## Screenshots

Screenshots from the live instance:

![Home page with continue-watching and trending anime](docs/images/home.jpg)

![Catalog with anime cards and filters](docs/images/catalog.jpg)

![Anime list with status and episode progress](docs/images/my-list.jpg)

## What you can do

- Track status, episode progress, score, and notes for each anime.
- Browse and search the AniList catalog by genre, year, season, format, and airing status.
- Import a list from AniList or a MyAnimeList XML export.
- Add administrator-managed source templates and save per-anime links.
- Share an opt-in, read-only profile by link; accounts are invite-only.
- Choose from three themes, each with dark, light, or system color mode.
- Optionally connect Plex for scrobbling (later-phase feature).

## Streaming sources

A source is a template configured by an administrator under **Sources (admin)**:

- **Base URL** — the site's home, such as `https://example.com`.
- **Search pattern** — a URL with `{title}`.
- **Episode pattern** — a URL with `{episode}` and an identifier such as `{anilist_id}`, `{mal_id}`, or `{slug}`.
- **Embed pattern** (optional) — an embed URL for the watch page.

Sources are not bundled. For sites that need an anime-specific slug, add a link by searching the site or pasting its URL. See the [admin guide](docs/admin-guide.md#streaming-sources).

## Documentation

| I want to… | Read |
|---|---|
| Use AniTether | [User guide](docs/user-guide.md) |
| Manage users and sources | [Admin guide](docs/admin-guide.md) |
| Run it on my own server | [Self-hosting guide](docs/self-hosting.md) |
| Maintain, back up, and update it | [Maintenance](docs/maintenance.md) |
| Troubleshoot a problem | [Troubleshooting](docs/troubleshooting.md) |
| Work on the code | [Development guide](docs/development.md) |

## Quick start (development)

Needs Docker, the .NET 10 SDK, and Node.js 20+.

```bash
docker compose up -d db
dotnet restore && npm --prefix web ci
ConnectionStrings__Default='Host=localhost;Port=5432;Database=anitether;Username=anitether;Password=anitether' \
  dotnet ef database update --project api
ConnectionStrings__Default='Host=localhost;Port=5432;Database=anitether;Username=anitether;Password=anitether' \
  Bootstrap__AdminEmail=you@example.com Bootstrap__AdminPassword='Change-me-1' \
  dotnet run --project api          # API on http://localhost:5000
npm --prefix web run dev            # site on http://localhost:5173
```

Log in at <http://localhost:5173> with the email and password you set above. More detail is in the [development guide](docs/development.md).

## Self-hosting

Production installs run as a Docker Compose stack with PostgreSQL, a one-shot migrator, the API, a daily backup, and an optional Cloudflare Tunnel. See [docs/self-hosting.md](docs/self-hosting.md).

## License

[GPLv3](LICENSE) — free to use, modify, and redistribute under the same terms.
