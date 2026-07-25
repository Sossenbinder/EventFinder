# EventFinder — web

Vite + React 19 + TypeScript + MapLibre GL frontend. Second consumer of the
`EventFinder.Api` store (`../src/EventFinder.Api`); see
`../specs/outlines/eventfinder-v1.md` for the product outline this implements.

## Development

```sh
npm install
npm run dev
```

The dev server proxies `/api` to the ASP.NET Core API (see `vite.config.ts`).
The API project ships no `Properties/launchSettings.json`, so `dotnet run
--project ../src/EventFinder.Api` binds Kestrel's bare default,
`http://localhost:5000` — confirmed by actually running it and reading the
"Now listening on" log line, not assumed. Start the API first, then `npm run
dev`. Override the proxy target with the `EVENTFINDER_API_PROXY_TARGET` env
var if the API ever runs on a different port.

## Build

```sh
npm run build
```

Runs `tsc -b` (strict mode, must pass with zero errors) then `vite build`,
producing `web/dist`. The repository root `Dockerfile` copies `web/dist` into
the API's `wwwroot` if it exists at build time (see the `frontend` stage in
`../Dockerfile`) — `npm run build` must be run before `docker build` for the
frontend to be included in the image.

## Basemap: tile source and attribution

The map uses **OpenFreeMap**'s `positron` vector style
(`https://tiles.openfreemap.org/styles/positron`) — free, requires **no API
key**, and matches `../../Mietmap/web`'s basemap choice (see
`Mietmap/web/src/map/MapView.tsx`). Underlying map data is OpenStreetMap.

Per OpenFreeMap's and OSM's terms, attribution is shown directly in the map
UI via MapLibre's `AttributionControl` (see `src/components/MapView.tsx`):

> Karte: © OpenFreeMap, Daten © OpenStreetMap-Mitwirkende

No environment variable or API key is needed for this tile source. If the
basemap is ever swapped for one that requires a key, read it from a
`VITE_`-prefixed env var (e.g. `VITE_MAPTILER_KEY`) — never commit it — and
update this section.

## Stack

- React 19, TypeScript (strict, no `any`), Vite 7
- `maplibre-gl` for the map (no `pmtiles`/vector-tile dependency needed here
  — event markers and the radius circle are drawn directly from GeoJSON, no
  vector basemap layer of the app's own)
- No state management library, no UI component framework: filter state
  lives in a handful of `useState` hooks in `App.tsx` and the URL query
  string (`src/urlState.ts`)

## Notes on the API contract consumed

- `GET /api/places?q=` — home-location autocomplete (gazetteer, ranked by
  population).
- `GET /api/events?lat=&lon=&radiusKm=&from=&to=&tags=&attendance=&limit=&offset=`
  — the map/list query.
- `GET /api/events.ics?...` — same filters, rendered as an iCalendar feed;
  the "Kalender abonnieren" button builds this URL from the current filters.
- `GET /api/events/unresolved` — events whose location the gazetteer
  couldn't resolve; never included in radius results, surfaced as a count on
  the Quellen (sources) tab.
- `GET /api/sources` — per-source last run/success, event count and last
  error, rendered on the Quellen tab.

All filters (home location, radius, date range, tags, attendance, active
tab) are kept in the URL query string (`src/urlState.ts`), so a search is
bookmarkable/shareable.
