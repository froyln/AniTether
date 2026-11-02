# AniTether

A self-hosted anime tracker (like MyAnimeList) that links out to streaming sites you
configure yourself. You keep a list of the anime you watch — status, episode progress,
score and notes — and from any entry jump straight to the next episode on a site, or watch
it inside AniTether's own embedded player page.

AniTether **ships with no sources**. The "streaming sites" are URL templates an admin adds
in the admin UI (name, base URL, search/episode/embed patterns). The app then builds
▶ buttons and an embedded watch page from those templates. Think of it the way an emulator
doesn't come with games: the tool is legal and content-free, you point it at whatever you
own the right to use.

It never stores, proxies, re-streams or hosts video. It stores your list, your progress and
links; the video always plays on the external site.

## Features

- **Your list** — Watching, Completed, Plan to watch, On hold, Dropped, Rewatching; episode
  progress, 0–10 score and notes.
- **Catalog** — search and browse all of AniList by genre, year, season, format and airing
  status; genre tiles with cover art.
- **Import** — pull in an existing list from AniList or a MyAnimeList XML export.
- **Sources** — admin-managed URL templates for any site. ▶ buttons, per-anime link saving,
  and an embedded watch page (`<iframe>` to the source's own player).
- **Plex** (optional) — later-phase scrobbling, per-user token.
- **Shareable profiles** — opt-in read-only list pages by link; no public signup.
- **Themes** — three colorways, each in dark/light/"match system".
- **Invite-only accounts** — Admins and Managers create users; there is no public signup.

## How sources work

A *source* is one row in the database describing how to build URLs for a site:

- `base_url` — the site's home, e.g. `https://example.com`.
- `search_url_template` — its search page with `{title}`.
- `episode_url_template` — an episode page with `{episode}` plus `{anilist_id}`, `{mal_id}`
  or `{slug}`.
- `embed_url_template` (optional) — an embed-only player URL, framed by the watch page.
- `language`, `sandboxed`, `allow_page_embed`, `enabled`.

Nothing is bundled. An admin adds sources through **Sources (admin)**; per-anime links (for
`{slug}` sites) are found by searching the site or pasted in manually. See the
[admin guide](docs/admin-guide.md#streaming-sources).

## Documentation

| I want to… | Read |
|---|---|
| Use the site | [User guide](docs/user-guide.md) |
| Manage users and sources | [Admin guide](docs/admin-guide.md) |
| Run it on my own server | [Self-hosting guide](docs/self-hosting.md) |
| Keep it running: backups, updates | [Maintenance](docs/maintenance.md) |
| Fix something that isn't working | [Troubleshooting](docs/troubleshooting.md) |
| Work on the code | [Development guide](docs/development.md) |

## Quick start (development)

Needs Docker, the .NET 10 SDK and Node.js 20+.

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

Log in at <http://localhost:5173> with the email and password you set above. Details in the
[development guide](docs/development.md).

## Self-hosting

Production installs run as a Docker Compose stack (PostgreSQL, one-shot migrator, the API,
a daily backup, optional Cloudflare Tunnel). See
[docs/self-hosting.md](docs/self-hosting.md).

## Legal note

AniTether is a tracker and a URL launcher. It ships with no sources and stores links and
progress only — it never fetches, hosts or streams media. You are responsible for the sites
you add and for complying with their terms of service and your local law.

## License

[GPLv3](LICENSE) — free to use, modify and redistribute under the same terms.
