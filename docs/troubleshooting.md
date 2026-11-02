# Troubleshooting

Find the symptom, then the fix. Self-hosted commands assume
`COMPOSE_FILE=docker-compose.prod.yml` in `.env` (otherwise add
`-f docker-compose.prod.yml`).

**Using the site**
- [Login says "Invalid email or password" but the password is right](#login-says-invalid-email-or-password-but-the-password-is-right)
- [No ▶ button for a site](#no--button-for-a-site)
- [The watch page's player stays empty or never plays](#the-watch-pages-player-stays-empty-or-never-plays)

**Accounts**
- [No admin can log in](#no-admin-can-log-in)
- [Everyone gets signed out after every restart](#everyone-gets-signed-out-after-every-restart)

**Self-hosted server**
- [`migrate` fails or the site never starts](#migrate-fails-or-the-site-never-starts)
- ["password authentication failed for user anitether_…"](#password-authentication-failed-for-user-anitether_)
- [Everyone gets "Too many requests" at once](#everyone-gets-too-many-requests-at-once)
- [Login works on the server but not from another computer](#login-works-on-the-server-but-not-from-another-computer)
- ["Pool overlaps with other one on this address space"](#pool-overlaps-with-other-one-on-this-address-space)
- [Cloudflare shows "Bad gateway" / 502](#cloudflare-shows-bad-gateway--502)
- [Harmless log line: "Cannot load library libgssapi_krb5.so.2"](#harmless-log-line-cannot-load-library-libgssapi_krb5so2)

**Development**
- [Every request fails with "The ConnectionString property has not been initialized"](#every-request-fails-with-the-connectionstring-property-has-not-been-initialized)
- [`dotnet restore` fails with NETSDK1226](#dotnet-restore-fails-with-netsdk1226)
- [The web app starts on port 5174 instead of 5173](#the-web-app-starts-on-port-5174-instead-of-5173)
- [Tests fail to start](#tests-fail-to-start)

---

## Using the site

### Login says "Invalid email or password" but the password is right
The login page shows that message for *any* failure, including the server not reaching its
database. Check the server log for database errors first (`docker compose logs api`, or
the terminal running `dotnet run`). Also: after 5 wrong passwords the account is locked
for 5 minutes, and an account an admin locked can't log in at all.

### No ▶ button for a site
- The site may not have this anime, or AniTether couldn't find it automatically. Open
  **Link this anime to a source site** on the anime page, find the anime on that site, and
  paste its address.
- The site may be disabled, or hidden because it failed its daily check three times (Admins:
  Source health page).
- The episode may not have aired yet (greyed out).

### The watch page's player stays empty or never plays
The player belongs to the external site; AniTether only shows it. Try another site under
**Source**, or **Open on site ↗**. Use **⚠ Report problem** so admins know. Ad blockers
and privacy extensions sometimes break these players.

## Accounts

### No admin can log in
- **Another Admin can still log in:** use the Users page to unlock the account or reset
  its password.
- **The only Admin is locked** (too many wrong passwords, or locked by mistake):
  ```bash
  docker compose exec db psql -U postgres anitether -c \
    "UPDATE \"AspNetUsers\" SET \"LockoutEnd\" = NULL, \"AccessFailedCount\" = 0 WHERE \"Email\" = 'you@example.com'"
  ```
- **The only Admin's password is lost:** the first-admin setting only works while no Admin
  exists. Remove the Admin role from everyone, let it create a new Admin, then fix your
  real account from the Users page:
  ```bash
  docker compose exec db psql -U postgres anitether -c \
    "DELETE FROM \"AspNetUserRoles\" WHERE \"RoleId\" = (SELECT \"Id\" FROM \"AspNetRoles\" WHERE \"Name\" = 'Admin')"
  ```
  Set `BOOTSTRAP_ADMIN_EMAIL` to a **new** address and `BOOTSTRAP_ADMIN_PASSWORD`, run
  `docker compose up -d`, log in with it, reset your real account's password and make it
  Admin again, then lock the temporary account.

(In development, use `docker compose exec db psql -U anitether anitether` instead, and start
the API with `Bootstrap__AdminEmail`/`Bootstrap__AdminPassword`.)

### Everyone gets signed out after every restart
The login encryption keys aren't being kept. With Docker, the `dp-keys` volume must exist
(`docker volume ls | grep dp-keys`); never run `docker compose down -v`, which deletes
volumes.

## Self-hosted server

### `migrate` fails or the site never starts
`docker compose logs migrate`. Usual causes: a password in `.env` containing `;` or `=`
(regenerate it with `openssl rand -hex 24`), or a database password changed in `.env` after
the first start (next entry).

### "password authentication failed for user anitether_…"
The database roles and their passwords are created once, on the database's first start.
If you changed `OWNER_DB_PASSWORD`, `APP_DB_PASSWORD` or `BACKUP_DB_PASSWORD` later, set
the same value inside the database:
```bash
docker compose exec db psql -U postgres -c "ALTER ROLE anitether_app PASSWORD '<value from .env>'"
```
(Likewise `anitether_owner`, `anitether_backup`.) `POSTGRES_PASSWORD` only matters on the
very first start.

### Everyone gets "Too many requests" at once
The server sees every visitor as the same address because it doesn't trust your proxy.
Check `TRUSTED_PROXY_NETWORKS`: `172.28.0.10/32` for the Cloudflare Tunnel,
`172.28.0.1/32` for a reverse proxy on the same machine. Behind Cloudflare with your own
proxy, also set `CLIENT_IP_HEADER=CF-Connecting-IP`. Values must be written as networks
(`1.2.3.4/32`, not `1.2.3.4`) — a bare address stops the server from starting.

### Login works on the server but not from another computer
The login cookie only works over HTTPS (or on the machine itself via `localhost`). Opening
`http://<server-ip>:8080` from another computer shows the page, but login never sticks.
Use an SSH tunnel (`ssh -L 8080:127.0.0.1:8080 you@server`, then `http://localhost:8080`)
or set up HTTPS as in the [self-hosting guide](self-hosting.md#step-5-put-it-online-optional).

### "Pool overlaps with other one on this address space"
Another Docker network already uses `172.28.0.0/24`. In `docker-compose.prod.yml` change,
together: the network's `subnet` and `ip_range`, and `cloudflared`'s `ipv4_address`
(keep it outside `ip_range`); then match `TRUSTED_PROXY_NETWORKS` in `.env`. For example
`172.29.0.0/24`, `172.29.0.128/25`, `172.29.0.10`, `172.29.0.10/32`.

### Cloudflare shows "Bad gateway" / 502
In the tunnel's Public Hostname, the service must be `HTTP` → `api:8080` (not
`localhost`, which would mean the tunnel container itself). Check
`docker compose logs cloudflared` and that `api` is running (`docker compose ps`).

### Harmless log line: "Cannot load library libgssapi_krb5.so.2"
The database driver checks for Kerberos support, which isn't used. Ignore it.

## Development

### Every request fails with "The ConnectionString property has not been initialized"
The API was started without a database connection string. Start it with:
```bash
ConnectionStrings__Default='Host=localhost;Port=5432;Database=anitether;Username=anitether;Password=anitether' \
  dotnet run --project api
```
and make sure the database is up (`docker compose up -d db`).

### `dotnet restore` fails with NETSDK1226
Happens on Arch-based systems (CachyOS, Manjaro…). `Directory.Build.props` already works
around it; if you still see it, make sure you're building from the repository root so that
file is picked up.

### The web app starts on port 5174 instead of 5173
Something (often an older `npm run dev`) is already using 5173. Vite picks the next free
port and prints it; use that address, or stop the old server.

### Tests fail to start
The integration tests start real PostgreSQL containers, so Docker must be running and your
user must be able to use it (`docker ps` works without `sudo`).
