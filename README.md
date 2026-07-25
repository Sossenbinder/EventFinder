# EventFinder

Tech meetups, user groups and small conferences in Germany — searchable by radius from any town.

The events happen outside the big cities. They're just scattered across dozens of Meetup groups,
Bevy chapter pages and hand-rolled calendars that no aggregator indexes, so from a small town it
*looks* like everything happens in Stuttgart or Berlin. EventFinder ingests those islands into one
store and lets you ask the only question that matters: **what's on within an hour's drive of me?**

Searching 75 km around Kirchheim unter Teck surfaces events in Fellbach, Ehningen, Backnang and
Pforzheim — places no "tech events near you" site will ever show you.

## What it does

- Ingests from a **curated registry** (`sources.yaml`) — 37 sources today: Meetup groups across the
  Stuttgart and Karlsruhe regions, plus German GDG chapters.
- **Offline geocoding** against a shipped German gazetteer — no geocoding API, no rate limits.
- **Radius search** with a map and list view, topic tags, date range and in-person/online filters.
- **Calendar subscription**: `/api/events.ics` renders your current filters as an iCalendar feed.
- A **transparency page** showing every source, its last run and its last error — including the count
  of events whose location could not be resolved, so coverage gaps stay visible instead of vanishing.

## Running it

```bash
dotnet run --project src/EventFinder.Api      # http://localhost:5000, ingests 15s after startup
```

```bash
dotnet run --project src/EventFinder.Api -- sources verify   # check every source still parses
dotnet run --project src/EventFinder.Api -- ingest once      # force a refresh
```

Frontend dev loop (Vite on :5173, proxying `/api` to :5000):

```bash
cd web && npm install && npm run dev
```

Tests — 95 of them, none touching the network (adapters parse recorded fixtures):

```bash
dotnet test EventFinder.slnx
```

## How it's built

`net10.0` throughout: `EventFinder.Core` (domain, normalization, dedupe, gazetteer — framework-free),
`EventFinder.Data` (EF Core + SQLite), `EventFinder.Ingestion` (source adapters), `EventFinder.Api`
(minimal API + background ingestion + serves the SPA). Frontend is Vite + React 19 + MapLibre GL.

Adding a source means one entry in `sources.yaml` — but only after `sources verify` proves it
fetchable and parseable. A registry that is honestly short beats one padded with URLs nobody checked.

Ingestion is **polite by construction**: robots.txt is honoured for every adapter (including its
`Crawl-delay`), conditional requests avoid refetching unchanged pages, and each source is isolated so
one broken adapter can never fail a run or empty the store.

## Attribution

- Place and postal-code data from [GeoNames](https://www.geonames.org/), licensed
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Regenerate with
  `scripts/build_gazetteer.py`.
- Basemap tiles by [OpenFreeMap](https://openfreemap.org/), data ©
  [OpenStreetMap contributors](https://www.openstreetmap.org/copyright).

Event data belongs to the organisers and platforms it comes from; EventFinder links back to the
original listing for every event and stores no personal data.
