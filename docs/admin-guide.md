# Admin guide

For accounts with the **Admin** or **Manager** role. Everyday use is in the
[user guide](user-guide.md); keeping the server healthy is in [maintenance](maintenance.md).

- [Roles](#roles)
- [Managing users](#managing-users)
- [Streaming sources](#streaming-sources)
- [Source health](#source-health)

## Roles

| | Admin | Manager | User (no role) |
|---|:-:|:-:|:-:|
| Use the site (list, watch) | ✓ | ✓ | ✓ |
| Create regular users, reset their passwords, lock/unlock them | ✓ | ✓ | |
| Give or remove the Admin or Manager role | ✓ | | |
| Manage Admins and Managers | ✓ | | |
| Streaming sources, source health | ✓ | | |

A Manager can never change their own role, touch another Manager or an Admin, or create
anyone with a role. The server checks all of this on every request, not just the page.

**The first Admin** is created when the server starts with `Bootstrap__AdminEmail` and
`Bootstrap__AdminPassword` set and no Admin exists yet. If that email already has an
account, it's promoted and keeps its password. After that the setting does nothing, and you
should remove the password from the configuration.

## Managing users

Account menu → **Users**.

- **Create user** — email, display name (up to 50 characters), a temporary password, and
  (Admins only) a role. Passwords need 6+ characters with upper and lower case, a digit and
  a symbol. Give the person the password; they change it under Settings → Account.
- **Role** (Admins only) — change it from the dropdown in the table.
- **Reset password** — set a new temporary password.
- **Lock / Unlock** — a locked user is signed out within about a minute and can't log in.
  Their list stays. **There is no delete**, on purpose: locking is reversible.

Role changes, locks and password resets take effect within a minute on the person's open
sessions — nobody has to log out and back in.

The last active Admin can't be demoted or locked, so you can't lock yourself out by
accident. If it happens anyway (for example, a lost password), see
[Troubleshooting → No admin can log in](troubleshooting.md#no-admin-can-log-in).

## Streaming sources

Account menu → **Sources (admin)** lists every site AniTether knows, with its language and
whether it's enabled. Nothing is bundled — an installation starts with an empty list and
you add the sites you use.

- **Enable / Disable** — a disabled site disappears from every page. Use it when a site
  dies or starts misbehaving.
- **Edit** — loads the site into the form below; change anything and **Save changes**.
  Clearing an optional address removes it. When a site moves to a new domain, change the
  base URL *and* every address in the same save — the server refuses addresses left on the
  old domain.
- **Add source** — for a new site. You need:
  - **Name**, **Base URL** (e.g. `https://example.com`) and **language** (English or
    Spanish).
  - **Search URL** — the site's search address with `{title}` where the search words go,
    e.g. `https://example.com/search?q={title}`. Required; used as the fallback link.
  - **Episode URL** (optional but what makes ▶ buttons work) — the address of one episode,
    with placeholders: `{episode}` (required), and one of `{anilist_id}`, `{mal_id}` or
    `{slug}`. Open an episode on the site and see which of those appears in its address.
    `{slug}` is the site's own name for the anime (e.g. `sousou-no-frieren`); AniTether
    finds it by searching the site, or users paste it via "Link this anime".
  - **Anime page URL** (rare) — only when the site's episode pages live under a different
    path than its anime pages (e.g. `/ver/…` vs `/anime/…`), so AniTether can recognise the
    anime page in search results.
  - **Embed URL** (optional) — an address that shows only the player, for the watch page.
  - **Sandboxed** — keep ticked unless the player refuses to run sandboxed; it blocks the
    player's popups and redirects.
  - **Allow page embed** — show the site's whole episode page inside the watch page. Only
    tick it after checking by hand that the player actually plays there.
- All addresses must be `https://` and on the site's own domain; the form rejects anything
  else.

When a site moves or changes its address pattern, see
[Maintenance → When a streaming site changes](maintenance.md#when-a-streaming-site-changes).

## Source health

Account menu → **Source health (admin)**.

- Once a day (and when you press **Check all sources now**, or **Check** on one site)
  AniTether checks each enabled site: does the home page load, does search work, does an
  episode page or player still load and still allow being shown inside AniTether.
- Each check shows **ok**, **broken** or **unknown**, with the HTTP status and a hint.
- A site that fails **3 checks in a row** is hidden from users until a check passes again.
  Admins still see it.
- **User reports** lists every "⚠ Report problem" from the watch page: which site, which
  anime and episode, and when.
- If email is configured (`Smtp__*` settings), Admins get one email per run listing the
  sites that changed state.
