# EventFinder — Agent Notes

## What this is

Aggregator for German tech events (meetups, user groups, small conferences) with radius search
from any German town. Built because the events *do* exist outside the big cities — they are just
scattered across dozens of ICS feeds, Bevy chapter pages and hand-rolled HTML calendars that no
existing aggregator indexes. Ingestion is Germany-wide; the radius is a query-time filter.

Outline: `specs/outlines/eventfinder-v1.md`. Read it before changing ingestion or the data model.

## Workflow: delegate execution to cheap models

Implementation work is dispatched to cost-appropriate executor subagents (haiku for mechanical
tasks, sonnet for well-specified work) with precise briefs and a stop-and-report rule. Kept in the
main session: **data-source judgment**, design decisions, diff review, and browser verification.

**Never invent a source URL.** Every `sources.yaml` entry must be proven fetchable by
`sources verify` before it is committed. A registry that is honestly short beats one padded with
plausible-looking URLs.

## Data sources

- **`gdg.community.dev` (Bevy) — via sitemaps, NOT the JSON API.** Verified 2026-07-26.
  **Do not reintroduce the `/api/` adapter**: `https://gdg.community.dev/robots.txt` says
  `Disallow: /api/` with `Crawl-delay: 2`. This aggregator is meant to be publicly deployed under
  its own name, so the stated preference is honoured even though the JSON endpoints are
  unauthenticated and technically reachable.
  What the API would have given (recorded so nobody re-derives it): `/api/search/event/` returns
  65,063 events with `_geoloc` already geocoded; **the only filter it honours is `chapter_id`** —
  `country_code`, `search`, `aroundLatLng` and friends are silently ignored (a bogus `country_code=ZZ`
  returns the same count), pagination caps at ~1000 results, and `result_types=upcoming_event` is
  ignored once `chapter_id` is set, so it happily returns events from 2017.
  The shipped path instead: `/sitemap.xml` → `sitemap-events-YYYY-MM.xml` (one per month, out to
  2027-04, only 64-193 URLs each, 1,119 across the next 12 months) → keep URLs matching
  `-<chapter-slug>-presents-` for the 25 curated German chapter slugs in `sources.yaml` → fetch each
  event page and read its single schema.org `Event` JSON-LD (`name`, `startDate`, `endDate`,
  `description`, `location.address.{streetAddress, addressLocality, postalCode}`).
  Two traps in that JSON-LD: `addressCountry` is wrong (a Karlsruhe venue reports `"US"`) so ignore
  it and geocode from `postalCode`/`addressLocality`; and `location` is sometimes a single `Place`
  and sometimes an array mixing `VirtualLocation` + `Place` for hybrid events.
  `sitemap-chapters.xml` (5 pages) enumerates all ~2,015 chapters if the curated slug list ever needs
  rebuilding — the API's chapter search caps out at 1,000 and cannot filter by country either.
- **Eventbrite — dead end.** Public event *search* (`GET /v3/events/search/`) was removed
  2019-12-12 and hard-disabled 2020-02-20. Only per-event-ID, per-venue and per-organization
  lookups remain, which require knowing the organizer up front. Do not build an Eventbrite
  adapter; do not reintroduce it in a later version without checking the changelog first.
- **Meetup — no usable free API, but the group pages are fair game.** The open REST API is retired;
  the GraphQL replacement needs OAuth plus a paid Pro entitlement. `https://www.meetup.com/robots.txt`
  disallows only `/files/`, `/fb/`, `/preview/`, `/n/*` and the atom/rss/xml calendar variants — a
  group's `/events/` page is **allowed**, so the curated per-group adapter is compliant.
  Structure (verified 2026-07-26): `<script id="__NEXT_DATA__">` → `props.pageProps.__APOLLO_STATE__`,
  a flat entity map. `Event:<id>` entries carry `title`, `dateTime` (ISO with offset), `endTime`,
  `eventUrl`, `eventType` (`PHYSICAL`/`ONLINE`/`HYBRID`) and `venue: {"__ref": "Venue:<id>"}`, which
  resolves into a `Venue:<id>` entry with `name`, `address`, `city`, `country`.
  Group *pages* carry no event JSON-LD — only the Apollo state has the data.
  Curated groups only, never platform-wide crawling. Expect this adapter to break most often; it must
  fail soft. Group slugs were discovered once, by hand, from the `__NEXT_DATA__` of
  `meetup.com/find/?location=de--<city>&source=GROUPS&keywords=<kw>` and then verified individually —
  that discovery step is deliberately not shipped as a crawler.
- **Meetup discovery, second sweep (2026-07-26).** 16 towns × 34 keywords over
  `meetup.com/find/?location=de--<city>&source=GROUPS&keywords=<kw>` plus the regional
  `meetup.com/cities/de/<city>/` pages, whose `pageProps.eventsInLocation` yields group slugs via
  each event's `eventUrl`. The city list itself comes from Meetup's own sitemap
  (`groups-index-sitemap.xml` → `sw_cities_1.xml.gz`, 332 German cities — note `/cities/us/de/` is
  Delaware, not Germany). 212 candidates, all reachable, 69 with upcoming events.
  **Find results cannot be paginated by URL**: the Apollo state exposes
  `pageInfo.hasNextPage`/`endCursor`, but the cursor is only consumed by the SPA's own GraphQL call,
  so each query returns at most 15 groups. Going wide on keyword × city is the workaround.
  The sweep pulls in plenty of non-tech (yoga, language cafés, Toastmasters, hiking, marketing,
  founder coaching) — selection is a human call, not something to automate.
- **lu.ma — real ICS feeds, low regional yield.** Every lu.ma calendar exports iCalendar at
  `https://api.lu.ma/ics/get?entity=calendar&id=<cal-api-id>` (no auth), and calendar ids are listed
  in `pageProps.initialData.calendars` on a city page such as `lu.ma/stuttgart`. But that page
  features *global* calendars, not local ones: the Google DeepMind calendar carried 164 events of
  which only 8 were future and 4 German. Worth revisiting if the German lu.ma scene grows — the
  `ics` adapter would consume these unchanged.
- **pretalx — parked, no location data.** `https://pretalx.com/api/events/` returns 729 public
  conferences (108 future, 24 with a German locale — Software-QS-Tag, Munich Embedded, IT-Summit …)
  with `slug`, `name`, `date_from`, `date_to`, `timezone`. **Neither the API detail endpoint nor the
  public conference page carries a city or venue**, so nothing here can be geocoded, and every entry
  would land permanently in the unresolved bucket, invisible to radius search. Only worth adding if
  location is inferred from the conference title.
- **Rejected after inspection** (do not spend time re-checking): `bwcon.de`, `startupstuttgart.de`
  and `codefor.de/stuttgart` publish no ICS and no schema.org `Event` markup — a bespoke HTML
  adapter each; `cyberforum.de` returns 403 to a non-browser UA. `events.shackspace.de` is a Vue SPA
  whose routes all return the same 1 KB shell, with no discoverable API; `eintopf.info` has a working
  `/rss` feed but is a cultural/political events site for Stuttgart, not a tech source; `jugs.org`,
  `ccc.de` and `events.ccc.de` expose no ICS/iCal endpoint at any of the conventional paths.
  No ICS source has been found yet — the `ics` adapter exists and is tested, but nothing in
  `sources.yaml` uses it.
- **Gazetteer — GeoNames, CC BY 4.0** (attribution required, redistribution permitted).
  `data/places-de.csv` (70,549 populated places: name, aliases, admin1, population, lat, lon) and
  `data/postal-de.csv` (10,813 postal codes) were generated 2026-07-25 by
  `scripts/build_gazetteer.py` from three dumps under `https://download.geonames.org/export/`:
  `dump/DE.zip`, `dump/alternatenames/DE.zip` and `zip/DE.zip`. Regenerate with that script; do
  not hand-edit the CSVs.
  **The alternate-names dump is not optional.** GeoNames' primary `name` column is inconsistent
  for German cities — it is the English exonym for Munich and Nuremberg but the German name for
  Köln — so a German venue string would never match "Munich". The build therefore takes the
  canonical name from the preferred `de` alternate name (58,003 places have one) and demotes the
  GeoNames primary name to an alias, so English spellings still resolve. `GazetteerTests`
  guards this for all three Munich spellings; do not regenerate without re-running them.
  Other wrinkles: some `postal-de.csv` rows are corporate large-customer PLZ whose `name` is a
  company, not a town (harmless for PLZ→coords, useless for name matching); GeoNames also ships
  3-letter airport-style aliases (`BER` for Berlin, `MUC` for Munich), so aliases shorter than 4
  characters are dropped at build time and alias matching requires a full-token match.

## Stack

`net10.0`, central package management (`Directory.Packages.props`), EF Core + SQLite,
ASP.NET Core minimal API, xUnit tests, Vite + React 19 + TypeScript + `maplibre-gl` in `web/`.
Mirrors `../FlatLens` (backend/ingestion) and `../Mietmap/web` (frontend).

## Rules that keep this maintainable

- **Per-source isolation.** One broken adapter must never fail a run or empty the store. Last-good
  data persists; the failure surfaces on `/sources`.
- **Adapter tests never hit the network.** Parse recorded fixtures under `tests/Fixtures/`.
- **Unresolved locations are kept, not dropped.** They are excluded from radius results and shown
  in a separate bucket, so coverage gaps stay visible instead of silently vanishing.
