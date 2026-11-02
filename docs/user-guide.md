# User guide

How to use AniTether, page by page. For managing users and sources see the
[admin guide](admin-guide.md).

- [Logging in](#logging-in)
- [The top bar](#the-top-bar)
- [Home](#home)
- [Catalog and Categories](#catalog-and-categories)
- [My list](#my-list)
- [Sharing your profile](#sharing-your-profile)
- [An anime's page](#an-animes-page)
- [The watch page](#the-watch-page)
- [Settings](#settings)
- [How progress is counted](#how-progress-is-counted)

## Logging in

AniTether is invite-only: an Admin or Manager creates your account and gives you a
temporary password. Log in with your email and that password, then change it under
**Settings → Account → Change password**.

After 5 wrong passwords in a row the account is locked for 5 minutes. If you've forgotten
your password, ask an Admin or Manager to reset it.

## The top bar

- **Home, Categories, Catalog, My list** — the main pages.
- **Search box** — searches the whole catalog by title.
- **Account menu** (your picture or initial) — Settings, Import list, Log out, and the
  admin pages if your account has a role.

## Home

- **Continue watching** — the anime on your list marked *Watching*. The ▶ button opens the
  next episode you haven't seen; **+1** marks one more episode watched.
- **Trending now**, **Popular this season**, **All-time top rated** — rows from AniList.
  Hover a cover and press **+** to add it to your list (as *Plan to watch*). **See all →**
  opens the same list in the catalog.

## Catalog and Categories

**Catalog** shows every anime on AniList. Filter by genre, format (TV, movie, OVA…), year,
season and airing status, and sort by best match, trending, most popular, top rated or
newest. Scroll down to load more. Your filters are kept in the address, so you can bookmark
or share a search.

**Categories** shows one tile per genre; click one to open the catalog filtered to it.

A **Coming soon** badge means the anime hasn't started airing yet.

## My list

Your whole list, with tabs per status (each showing how many anime it has). You can filter
by title and sort the list. Click an entry to open a dialog where you can change its
status, progress, score (0–10) and notes, open it on a site, or remove it from your list.

Use **Share profile** to preview your profile and copy its link. Sharing starts off. Anyone
with the link can view your profile while sharing is enabled.

## Sharing your profile

Under **Settings → Account → Profile**, upload a picture, change your display name, add a
short bio, and turn profile sharing on or off. Pictures are cropped to a square. The shared
page shows your name, picture, bio, anime, status, episode progress and score. Notes, email
and account settings remain private. Turning sharing off makes the link unavailable to
other people; your own preview remains available from **Share profile**.

The shared page is read-only and works without an AniTether account. Anime links open AniList
for visitors who are not signed in.

## An anime's page

Open any cover to see the anime's page:

- **Left panel** — your status, progress (with −1 / +1), score, and **+ Add to list** or
  **Remove from list**. The big **▶** button opens the next episode you haven't seen
  (**Start** / **Continue**), or, once you click an episode in the grid, that episode
  (**Play ep N**).
- **Episodes** — pick an episode from the grid. For anime still airing, episodes that
  haven't aired yet are greyed out. For anime that haven't started at all you see a
  **Coming soon** card instead.
- For the selected episode, two sections:
  - **Watch on** — one ▶ button per streaming site that has this episode, your preferred
    language first. Sites in other languages are folded under "… sites".
  - **Link this anime to a source site** — for sites AniTether couldn't find this anime on
    by itself. Paste the anime's (or any episode's) address on that site and save; the ▶
    button for that site appears, for everyone.

AniTether looks up most sites automatically the first time anyone opens the anime, so the
▶ buttons usually appear on their own after a moment.

## The watch page

▶ buttons open AniTether's own watch page whenever the site can be shown inside it;
otherwise the site opens in a new tab.

- The player is the external site's own player, shown inside AniTether.
- **Source** — switch to another site if one doesn't load or plays badly.
- **Sub / Dub** — switch between subtitles and dubbed audio where the site offers both.
  Your choice is remembered.
- **◀ Prev / Next ▶** — move between episodes. Next stops at the latest aired episode.
- **✓ Mark watched** — mark this episode watched now. If you stay on the page for
  **5 minutes**, it's marked automatically.
- **Open on site ↗** — open the same episode directly on the site.
- **⚠ Report problem** — tell the admins this player is broken; they see it on the Source
  health page. Separately, a site that fails AniTether's daily check three times in a row is
  hidden from everyone until it works again.

Players come from third-party sites and may show their own ads or popups; AniTether can't
control what's inside them.

## Settings

- **Import list** — bring in your list from **AniList** (enter your AniList username) or
  **MyAnimeList** (upload the XML export from MAL, up to 5 MB). Importing never lowers
  progress you already have; it reports how many entries were added, updated or skipped.
- **Appearance** — colour (Default violet, Crimson, Slate blue) and mode (Match system,
  Dark, Light). The change applies instantly and follows you to other devices.
- **Account** — edit your profile name, picture and bio; choose whether to share your list;
  set preferred language for streaming sites and preferred audio for the player; and
  **Change password**.

## How progress is counted

- Progress only goes up: marking episode 6 watched when you're at 8 changes nothing.
- An anime on *Plan to watch* moves to *Watching* when you watch an episode.
- Reaching the last episode moves it to *Completed* and records the finish date.
- AniTether doesn't see what happens inside a site's player; progress comes from +1, Mark
  watched, or the watch page's 5-minute auto-mark.
