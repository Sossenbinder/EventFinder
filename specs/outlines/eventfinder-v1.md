# Outline: EventFinder V1 — German tech-event aggregator with radius search

## Context

Tech meetups, user groups and small conferences do happen outside the big cities — JUG chapters, CCC Erfa circles, hackerspaces, Hochschule and IHK events, GDG chapters — but each publishes to its own island (ICS feed, Bevy page, plain HTML). No aggregator covers them, so from Kirchheim unter Teck it *looks* like everything happens in Stuttgart. The platform APIs that used to solve this are gone: Eventbrite killed public event search in Feb 2020, and Meetup's API is now OAuth + Pro-only. EventFinder is a greenfield project (empty repo) that ingests the islands into one store and serves an interactive radius search over them.

## Scope

**In scope:**

- Germany-wide ingestion from a curated `sources.yaml` registry; radius filtering happens at query time.
- Three adapter types: generic **ICS/iCal**, **Bevy JSON API** (verified live: `https://gdg.community.dev/api/search/event/?result_types=upcoming_event` returns paginated JSON with `_geoloc`, `venue_city`, `venue_zip_code`, `start_date_iso`), and **per-source HTML** adapters.
- Offline geocoding via a shipped German gazetteer (place name + PLZ → lat/lon).
- Normalization, cross-source dedupe, and an event store (SQLite via EF Core).
- ASP.NET Core minimal API + interactive React/MapLibre frontend: map + list, home-location search, radius slider, date range, topic tags, in-person/online filter.
- `GET /api/events.ics` — subscribe to your filtered radius as a calendar feed.
- A `/sources` transparency view: every source, last fetch, event count, last error.
- Dockerfile + compose so it is deployable to the web (Hetzner + Caddy, per `EntnahmeplanSuite` precedent).

**Out of scope:**

- Platform-wide scraping of Meetup or Eventbrite (see Key Decisions for the curated-group exception).
- LLM-assisted extraction of arbitrary event pages — explicitly rejected; costs tokens per crawl and hallucinates dates.
- User accounts, RSVPs, notifications, email/Telegram digests, event submission forms.
- Non-tech event categories; the registry defines "tech" by curation, not by classification.
- Actual VPS provisioning and DNS.

## Approach

Ingestion is the product; the UI is a second consumer of the same store.

**Solution layout** (`EventFinder.slnx`, `net10.0`, central package management mirroring `FlatLens/Directory.Packages.props`):

- `src/EventFinder.Core` — domain model (`Event`, `Source`, `Place`), normalization, dedupe, haversine radius query, gazetteer lookup. Framework-free.
- `src/EventFinder.Ingestion` — `IEventSource` adapters returning `RawEvent`; `IcsSource` (Ical.Net), `BevySource` (JSON), `HtmlSource` (AngleSharp, per-source parser keyed by adapter name in DI). Registry loaded from `sources.yaml` (YamlDotNet).
- `src/EventFinder.Api` — minimal API, hosts the ingestion `BackgroundService` (default every 6h, jittered, per-source `ETag`/`Last-Modified` honored), serves the built SPA as static files.
- `web/` — Vite + React 19 + TypeScript + `maplibre-gl`, matching `Mietmap/web/package.json`.
- `tests/EventFinder.Tests` — xUnit; adapter parsing against recorded fixture files (never live network), plus normalization/dedupe/geo unit tests.

**Data flow:** `sources.yaml` → adapter fetch → `RawEvent` → normalize (trim, strip boilerplate, resolve timezone, tag from source + title keywords) → geocode → dedupe → upsert into SQLite → API query by bounding box + haversine → React map/list.

**Geocoding cascade:** explicit coordinates from the source (Bevy `_geoloc`, ICS `GEO`) → PLZ extracted from the address string → city-name match against the gazetteer → unresolved. Unresolved events are stored and shown in a separate "location unknown" bucket, never silently dropped and never included in radius results.

**Registry honesty rule:** every `sources.yaml` entry must be proven fetchable by a `dotnet run --project src/EventFinder.Api -- sources verify` command before it is committed. URLs that cannot be verified do not go in the file. `AGENTS.md` gets a "Data sources" ledger in the style of `Mietmap/AGENTS.md`, recording what is obtainable and what is blocked and why.

## Key Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Product shape | Interactive web app from V1, deployable | User override of the earlier CLI/digest answer: "V1 needs to be an interactive frontend. In the end I want to deploy this on the web." No CLI digest, no notifications. |
| Geographic model | Ingest Germany-wide, filter by radius at query time | Ingestion cost is per-source, not per-km. A national corpus makes the tool useful to anyone and costs nothing extra locally. |
| Geocoding | Offline gazetteer (GeoNames/OpenPLZ German extract) | Deterministic, no rate limits, no network at query time. Town-level precision is sufficient for a radius; Nominatim's 1 req/s policy is not worth the coupling. |
| Store | SQLite + EF Core | FlatLens precedent, read-mostly workload, tiny corpus, one file to back up and deploy. Postgres only if multi-writer ingestion ever appears. |
| Meetup | Curated per-group HTML adapters only | Excluding Meetup entirely would gut coverage of exactly the small local groups this tool exists to find. A registry of specific group URLs is an HTML adapter (the chosen strategy), not platform-wide scraping. Flag it as the most breakage-prone type and let it fail soft. |
| Eventbrite | Excluded, documented | Public search API removed Feb 2020; only per-organizer/venue lookups remain. Nothing to build against. |
| Dedupe key | `hash(normalized title + start date (day) + resolved city)`, plus a `(source, sourceId)` unique index | Same meetup cross-posted to Bevy and an ICS feed must collapse to one card; keep the earliest-seen record and merge the URL list. |
| Failure policy | Per-source isolation | One broken adapter must never fail a run or empty the store; last-good data persists, the error surfaces on `/sources`. |
| Map tiles | MapLibre GL with a free raster/vector basemap, tile source recorded in the README | Matches Mietmap's stack; attribution/licence is a deploy prerequisite, not an afterthought. |
| "Tech" definition | Curation, not classification | Sources are admitted only if they are tech by nature; topic tags are a filter nicety, not a gatekeeper. Avoids building a classifier. |

## Trade-offs

- **Coverage vs. maintenance:** hand-written HTML adapters break silently when sites redesign. Accepted, mitigated by per-source isolation and a visible `/sources` health page.
- **Precision vs. independence:** town-level geocoding will misplace a venue by a few km. Accepted — for a "can I get there on a Tuesday evening" question this is noise.
- **Breadth vs. honesty:** national ingestion with ~30 sources means coverage is thin outside the seeded regions. Prefer an obviously incomplete source list over a padded one with guessed URLs.
- **Speed vs. reach:** skipping Meetup platform-wide and Eventbrite entirely leaves real events undiscovered. Accepted as the price of a maintainable, low-legal-risk V1.

## Execution profile

- **Tier:** sonnet — multi-dispatch, with source curation retained in-session
- **Rationale:** the app is well-specified scaffolding over established workspace patterns (net10.0, EF Core/SQLite, minimal API, Vite/React/MapLibre), so the code translation carries no design judgment. Separable workstreams: (1) Core + store + gazetteer, (2) ingestion adapters + registry loader, (3) API + ICS export, (4) React frontend. Per `Mietmap/AGENTS.md`, **data-source judgment stays in the main session** — I seed and verify `sources.yaml` and review every diff.
- **Escalation triggers:** the executor stops and reports if — a source returns a shape the adapter contract cannot express; fewer than 10 registry entries verify as fetchable; dedupe cannot be made deterministic without a new heuristic; the gazetteer dataset's licence forbids redistribution in the repo; or any question arises that this outline does not answer. No improvised design decisions, no invented source URLs.

## Open Questions

- **Ingestion cadence under real load** — 6h is a guess; the right interval depends on how often the seeded sources actually change, observable only after a week of running.
- **Coverage beyond Meetup and GDG** — no ICS feed has been found yet for the local groups that would matter most (shackspace, JUGS, CCC Erfa circles); each needs individual investigation and several publish no machine-readable calendar at all.

### Resolved during implementation

- **Gazetteer dataset** — GeoNames (CC BY 4.0, redistribution permitted); 7.4 MB of dumps reduced to 3 MB of committed CSV. The per-country *alternate names* dump turned out to be mandatory: GeoNames' primary name column holds the English exonym for Munich and Nuremberg but the German name for Köln.
- **Basemap** — OpenFreeMap `positron`, no API key, matching Mietmap; attribution rendered in-map and documented in `web/README.md`.
- **robots.txt vs. the cornerstone source** — `gdg.community.dev` disallows `/api/`, the endpoint the original Bevy adapter used. Resolved in favour of the sanctioned sitemap + JSON-LD path; the AGENTS.md ledger records the full reasoning and what the API would have offered.
