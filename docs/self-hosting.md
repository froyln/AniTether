# Self-hosting AniTether

Install AniTether on your own server (a homelab box, a NAS with Docker, a small VPS) with
Docker Compose. At the end you have the site running, a daily database backup, and
optionally a public HTTPS address through Cloudflare.

Once it's running: [Maintenance](maintenance.md) (backups, updates) ·
[Troubleshooting](troubleshooting.md) · [Admin guide](admin-guide.md).

- [Before you start](#before-you-start)
- [Step 1: Get the code](#step-1-get-the-code)
- [Step 2: Configure](#step-2-configure)
- [Step 3: Start it](#step-3-start-it)
- [Step 4: First login](#step-4-first-login)
- [Step 5: Put it online (optional)](#step-5-put-it-online-optional)
- [Optional extras](#optional-extras)
- [What's running and why](#whats-running-and-why)
- [Legal note](#legal-note)

## Before you start

You need:

- A Linux machine with **Docker Engine 24+** and the **Compose plugin**
  (`docker compose version` should work). x86-64 and ARM64 (e.g. Raspberry Pi 4/5) both
  work.
- About **1 GB of free RAM** and a few GB of disk.
- **Internet access** from the server: it looks up anime on AniList and searches the
  sites you configure.
- `git` and `openssl`.
- Only for a public address: a domain managed by a free **Cloudflare** account.

## Step 1: Get the code

```bash
git clone https://github.com/froyln/anitether.git
cd anitether
```

## Step 2: Configure

All settings live in one file, `.env`, in the repository folder. Start from the template
and keep it private:

```bash
cp deploy/env.example .env
chmod 600 .env
```

**Database passwords.** AniTether uses four separate database accounts so that each part
can only do what it needs (explained [below](#whats-running-and-why)). Generate all four
passwords in one go — letters and digits only, since they go into connection strings:

```bash
for v in POSTGRES_PASSWORD OWNER_DB_PASSWORD APP_DB_PASSWORD BACKUP_DB_PASSWORD; do
  sed -i "s/^$v=\$/$v=$(openssl rand -hex 24)/" .env
done
```

**First admin.** Open `.env` in an editor and set:

```ini
BOOTSTRAP_ADMIN_EMAIL=you@example.com
BOOTSTRAP_ADMIN_PASSWORD=Your-Strong-Pass1
```

The password needs at least 6 characters with an uppercase letter, a lowercase letter, a
digit and a symbol.

**Shorter commands (recommended).** Add this line to `.env` so every command in these
guides works without `-f docker-compose.prod.yml`:

```ini
COMPOSE_FILE=docker-compose.prod.yml
```

Everything else in `.env` has a working default. The full list, each explained in the file
itself:

| Setting | Default | What it's for |
|---|---|---|
| `API_BIND`, `API_PORT` | `127.0.0.1`, `8080` | Where the site listens. Keep `127.0.0.1`: login only works over HTTPS or on the server itself, so opening it to the LAN over plain http doesn't help. |
| `TUNNEL_TOKEN` | empty | Cloudflare Tunnel, [step 5](#option-a-cloudflare-tunnel-recommended). |
| `TRUSTED_PROXY_NETWORKS`, `CLIENT_IP_HEADER` | tunnel defaults | How the server learns each visitor's real IP behind a proxy. Only change for [option B](#option-b-your-own-reverse-proxy). |
| `BACKUP_DIR`, `BACKUP_KEEP_DAYS` | `./backups`, `14` | Where daily backups go and how long they're kept. |
| `HEALTH_CHECKS_ENABLED` | `true` | Daily check that the configured sites still work. |
| `SMTP_*` | empty | Email Admins when a site breaks. [Optional extras](#email-alerts). |

## Step 3: Start it

```bash
docker compose up -d --build
```

The first build takes a few minutes. Then check:

```bash
docker compose ps
```

You should see `db`, `api` and `backup` **Up**, and `migrate` **Exited (0)** — that one
sets up the database and stops, which is expected.

## Step 4: First login

On the server itself, open <http://127.0.0.1:8080> and log in with the admin email and
password from step 2. From another computer, until step 5 is done, use an SSH tunnel:

```bash
ssh -L 8080:127.0.0.1:8080 you@your-server
# then open http://localhost:8080 on your computer
```

Then:

1. **Remove the admin password from `.env`** (leave `BOOTSTRAP_ADMIN_PASSWORD=` empty) and
   run `docker compose up -d`. The setting only ever works while no Admin exists, but it
   shouldn't sit in a file.
2. Change your password under **Settings → Account**.
3. Create accounts for other people under the account menu → **Users** (see the
   [admin guide](admin-guide.md#managing-users)).
4. Add your streaming sources under **Sources (admin)** (see the
   [admin guide](admin-guide.md#streaming-sources)).
5. **Set up off-server backups** — see [Maintenance → Backups](maintenance.md#backups).

## Step 5: Put it online (optional)

Skip this if you only use AniTether at home through the SSH tunnel. Otherwise pick one:

- **Option A — Cloudflare Tunnel.** Recommended for homelabs: no ports opened on your
  router, your home IP stays hidden, works even without a public IP.
- **Option B — your own reverse proxy** (Caddy, nginx, Traefik) with ports 80/443
  forwarded, if you already run one.

### Option A: Cloudflare Tunnel (recommended)

1. Add your domain to Cloudflare, so Cloudflare manages its DNS.
2. In the Cloudflare dashboard: **Zero Trust → Networks → Tunnels → Create a tunnel** →
   **Cloudflared** → give it a name (e.g. `anitether`).
3. On the "Install connector" screen choose **Docker** and copy only the token — the long
   text after `--token`. Put it in `.env`:
   ```ini
   TUNNEL_TOKEN=eyJh...
   ```
4. In the tunnel's **Public Hostname** tab, add one:
   - Subdomain and domain: e.g. `anime.example.com`
   - Service type **HTTP**, URL **`api:8080`**
5. Start the tunnel along with the rest:
   ```bash
   docker compose --profile tunnel up -d
   ```
6. Open `https://anime.example.com`.

Recommended in Cloudflare for your domain: **SSL/TLS → Edge Certificates → Always Use
HTTPS** on, and **Minimum TLS Version** 1.2. For extra privacy, put **Zero Trust → Access**
in front of the address so only people you list can even reach the login page.

The tunnel defaults in `.env` already match this setup; nothing else to change.

### Option B: your own reverse proxy

1. Cloudflare → **SSL/TLS → Origin Server → Create Certificate**. Save the certificate and
   key on the server, readable only by the proxy (e.g. `/etc/caddy/origin.pem` and
   `/etc/caddy/origin.key`).
2. Cloudflare → **SSL/TLS → Overview** → mode **Full (strict)**.
3. DNS: an `A` (or `AAAA`) record for `anime.example.com` → your public IP, **proxied**
   (orange cloud).
4. Proxy config — for Caddy, `/etc/caddy/Caddyfile`:
   ```caddy
   anime.example.com {
       tls /etc/caddy/origin.pem /etc/caddy/origin.key
       reverse_proxy 127.0.0.1:8080
   }
   ```
5. In `.env`, then `docker compose up -d`:
   ```ini
   TRUSTED_PROXY_NETWORKS=172.28.0.1/32
   CLIENT_IP_HEADER=CF-Connecting-IP
   ```
   The first line lets the proxy on this machine report visitors' IPs; the second reads the
   visitor's IP from Cloudflare's header (otherwise every visitor looks like Cloudflare and
   they all share one login rate limit).
6. **Only let Cloudflare reach your server.** Anyone connecting to your IP directly could
   fake the `CF-Connecting-IP` header. Firewall ports 80/443 to
   [Cloudflare's IP ranges](https://www.cloudflare.com/ips/), or enable **Authenticated
   Origin Pulls** (SSL/TLS → Origin Server) and require it in the proxy.

## Optional extras

### Email alerts

To email Admins when a configured site breaks or recovers, fill in `SMTP_HOST`, `SMTP_PORT`
(usually 587), `SMTP_USER`, `SMTP_PASSWORD` and `SMTP_FROM` with your mail provider's SMTP
details, then `docker compose up -d`.

## What's running and why

| Container | What it does |
|---|---|
| `db` | PostgreSQL 17 with all data. Only reachable by the other containers — no port is opened on the server. |
| `migrate` | Runs at every start, updates the database structure if needed, then stops. The site waits for it. |
| `api` | AniTether itself: the website and its API, on port 8080 inside the container. |
| `backup` | Dumps the database once a day into `BACKUP_DIR` and deletes old dumps. |
| `cloudflared` | Only with `--profile tunnel`: the Cloudflare Tunnel. |

Security, in short:

- **Separate database accounts.** The site runs as `anitether_app`, which can read and write
  rows but can't change or delete tables. Only the short-lived `migrate` container (as
  `anitether_owner`) changes the structure. Backups run as `anitether_backup`, which can only
  read. The Postgres superuser is used only inside the database container (first start and
  restores).
- **Backups** are readable by root only: they contain emails and password hashes.
- **Invite-only accounts:** no public signup; Admins and Managers create accounts.
- **Logins survive restarts:** the encryption keys for login cookies are kept in the
  `dp-keys` volume.
- **Errors** show visitors a generic message; details only go to the log.
- **Real visitor IPs:** the server only trusts the proxy address in
  `TRUSTED_PROXY_NETWORKS` to say who the visitor is, so login rate limits work per visitor.

## Legal note

AniTether is a tracker and a URL launcher: it ships with no sources and only stores your
list, progress and the links you or your admin configure. It never fetches, hosts or
streams media. You are responsible for the sites you add and for complying with their terms
of service and your local law. Keep the instance private (it's login-only, with no public
signup) and only point it at content you're entitled to use.
