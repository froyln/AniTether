# Maintenance

Keeping AniTether healthy once it's running. Commands assume the Docker setup from the
[self-hosting guide](self-hosting.md) with `COMPOSE_FILE=docker-compose.prod.yml` in `.env`,
so plain `docker compose …` works. Without it, add `-f docker-compose.prod.yml`.
For the development setup, see the [notes at the end](#on-a-development-machine).

- [Routine checklist](#routine-checklist)
- [Backups](#backups)
- [Restoring a backup](#restoring-a-backup)
- [Updating AniTether](#updating-anitether)
- [When a streaming site changes](#when-a-streaming-site-changes)
- [Logs and monitoring](#logs-and-monitoring)
- [Passwords and secrets](#passwords-and-secrets)
- [Disk space](#disk-space)
- [On a development machine](#on-a-development-machine)

## Routine checklist

| How often | What | Where |
|---|---|---|
| Weekly | Look at **Source health** for broken sites and user reports; disable or fix them | [Admin guide](admin-guide.md#source-health) |
| Weekly | Check that new backup files keep appearing and are copied off the server | [Backups](#backups) |
| Monthly | Update AniTether and its container images | [Updating](#updating-anitether) |
| Every few months | Restore a backup into a scratch copy to prove backups work | [Restoring](#restoring-a-backup) |
| When a site moves or breaks | Update or disable its source | [Streaming sites](#when-a-streaming-site-changes) |

## Backups

The `backup` container runs `pg_dump` once a day (and once right after startup) and writes
`anitether-YYYY-MM-DD_HHMM.dump` into `BACKUP_DIR` (default `./backups`). Dumps older than
`BACKUP_KEEP_DAYS` (default 14) are deleted. The files are readable by root only, because
they contain emails and password hashes.

- **Check it's working:** `ls -l backups/` shows a file from today;
  `docker compose logs backup` shows `backup ok:` lines. A failed dump never replaces a
  good one and logs `backup FAILED`.
- **Copy backups off the server.** A backup on the same disk doesn't survive that disk
  failing. Use `rsync`/`rclone` from cron, or include the folder in your existing backup
  tool (restic, Borg, …):
  ```cron
  30 4 * * * rclone copy /path/to/anitether/backups remote:anitether-backups
  ```
- **Take one by hand** (before an update, for example):
  ```bash
  docker compose exec backup \
    sh -c 'umask 077; pg_dump -Fc -h db -d "$POSTGRES_DB" -f /backups/manual-$(date +%F).dump'
  ```
- **Also keep safe:** your `.env` (all passwords; store it separately from the dumps). The
  `dp-keys` volume isn't backed up and doesn't need to be: losing it only signs everyone
  out once.

## Restoring a backup

```bash
scripts/restore.sh backups/anitether-2026-09-28_0400.dump
```

This **replaces everything in the database** with the backup. It stops the site, restores,
and starts the site again. The whole restore is one transaction: if anything goes wrong
(a damaged file, for example) the database is left exactly as it was. Restoring an older
backup after an update also removes tables the newer version added, so it works as a
rollback.

To test a backup without touching the real data, restore it into a scratch database next
to the real one, look at it, and remove it:

```bash
docker compose exec -T db createdb -U postgres restoretest
docker compose exec -T db pg_restore -U postgres --no-owner -d restoretest < backups/<file>.dump
docker compose exec db psql -U postgres restoretest -c 'SELECT count(*) FROM "AspNetUsers"; SELECT count(*) FROM list_entries;'
docker compose exec -T db dropdb -U postgres restoretest
```

If the counts look right, the backup is good.

## Updating AniTether

```bash
cd anitether
docker compose exec backup \
  sh -c 'umask 077; pg_dump -Fc -h db -d "$POSTGRES_DB" -f /backups/pre-update-$(date +%F).dump'
git pull
docker compose pull            # newer Postgres / cloudflared images
docker compose up -d --build
docker compose ps              # migrate: Exited (0); api, db, backup: Up
```

Database changes are applied automatically by the `migrate` container before the site
starts. If `migrate` fails, the site doesn't start and the data is untouched; read
`docker compose logs migrate`.

**Rolling back:** `git checkout <previous version>`, restore the `pre-update-…` dump,
then `docker compose up -d --build`.

## When a streaming site changes

Streaming sites move domains, change their addresses and disappear often. Signs: the
Source health page shows it broken, users report problems, or ▶ buttons lead to errors.

1. **Disable it first** (Sources page → Disable) so nobody hits a dead site.
2. Open the site in your browser and find where it went: new domain, or a new address
   pattern for episodes.
3. **Update its addresses:** Sources page → **Edit** on that site → change the base URL and
   every address to the new domain or pattern → **Save changes**. The page refuses a save
   that would leave an address on the old domain or on a different site.
4. **Enable it** again and press **Check** on the Source health page.

Saved per-anime links for a `{slug}` site keep their old domain; if the domain changed,
users can re-link from the anime page, or clear the old links so they're found again:
`DELETE FROM anime_source_links WHERE "SourceId" = '<the source id>';`

## Logs and monitoring

- **Logs:** `docker compose logs -f api`. Each line is one JSON object with `LogLevel`,
  `Category` and `Message`, so log tools (Dozzle, Loki, Graylog) read them as-is. Only
  problems:
  ```bash
  docker compose logs api | grep -E '"LogLevel":"(Warning|Error|Critical)"'
  ```
- **Health endpoint:** `GET /api/health` answers `200 {"status":"ok"}`, or `503` when the
  database is unreachable. Point Uptime Kuma or any uptime checker at it.
- **Errors never reach visitors:** they see a generic message; the details are in the log.
- **Source health emails:** set the `SMTP_*` values in `.env` to get an email when a
  streaming site breaks or recovers.

## Passwords and secrets

All secrets live in `.env` (never in the code or the repository).

- **App's first-admin password:** remove `BOOTSTRAP_ADMIN_PASSWORD` after the first login.
- **Database passwords:** the database keeps the passwords it was *created* with. To change
  one, change it in both places:
  ```bash
  docker compose exec db psql -U postgres -c "ALTER ROLE anitether_app PASSWORD '<new>'"
  # then put the same value in .env as APP_DB_PASSWORD and: docker compose up -d
  ```
  Same for `anitether_owner` (`OWNER_DB_PASSWORD`) and `anitether_backup`
  (`BACKUP_DB_PASSWORD`). Use letters and digits only (`openssl rand -hex 24`).
- **SMTP:** change them in `.env`, then `docker compose up -d`.
- **Users:** Admins and Managers reset passwords from the Users page.

## Disk space

- **Backups:** lower `BACKUP_KEEP_DAYS` if `backups/` grows too large.
- **Old container images** pile up after updates: `docker image prune` removes the unused
  ones.
- **Logs:** Docker keeps container logs until the container is recreated. To cap them, add
  a `logging:` section with `max-size`/`max-file` to the services in
  `docker-compose.prod.yml`, or set it globally in Docker's `daemon.json`.
- The database itself stays small (lists, links and cached anime data).

## On a development machine

- The development database is the `db` service in `docker-compose.yml`, with its data in a
  Docker volume. `docker compose down` keeps the data; `docker compose down -v` deletes it.
- After pulling new code, apply database changes:
  `ConnectionStrings__Default='Host=localhost;Port=5432;Database=anitether;Username=anitether;Password=anitether' dotnet ef database update --project api`
- There are no automatic backups in development. Take one by hand if the data matters:
  `docker compose exec db pg_dump -U anitether -Fc anitether > dev-backup.dump`
