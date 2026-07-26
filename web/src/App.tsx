import { useEffect, useMemo, useState } from 'react'
import { ApiError, getSources, queryEvents } from './api'
import EventList from './components/EventList'
import FilterPanel from './components/FilterPanel'
import HomeLocationPicker from './components/HomeLocationPicker'
import MapView from './components/MapView'
import SourcesPanel from './components/SourcesPanel'
import SubscribeButton from './components/SubscribeButton'
import { formatCount } from './format'
import type { EventDto, Filters, PlaceDto, SourceStatusDto } from './types'
import { parseStateFromUrl, stateToUrl, type View } from './urlState'

const initial = parseStateFromUrl(window.location.search)

export default function App() {
  const [filters, setFiltersState] = useState<Filters>(initial.filters)
  const [view, setView] = useState<View>(initial.view)

  const [events, setEvents] = useState<EventDto[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [eventsLoading, setEventsLoading] = useState(true)
  const [eventsError, setEventsError] = useState<string | null>(null)
  const [eventsReloadToken, setEventsReloadToken] = useState(0)

  const [sources, setSources] = useState<SourceStatusDto[] | null>(null)
  const [sourcesLoading, setSourcesLoading] = useState(true)
  const [sourcesError, setSourcesError] = useState<string | null>(null)
  const [sourcesReloadToken, setSourcesReloadToken] = useState(0)

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [hoveredId, setHoveredId] = useState<string | null>(null)

  function patchFilters(patch: Partial<Filters>) {
    setFiltersState((prev) => ({ ...prev, ...patch }))
  }

  function pickPlace(place: PlaceDto) {
    patchFilters({ lat: place.latitude, lon: place.longitude, placeName: place.name })
  }

  // Deep-linkable state: every filter (and the active tab) lives in the URL
  // query string, kept in sync via replaceState so a search can be
  // bookmarked or shared without piling up history entries per keystroke.
  useEffect(() => {
    history.replaceState(null, '', stateToUrl(filters, view))
  }, [filters, view])

  // Debounced so dragging the radius slider or typing a tag doesn't fire a
  // request per intermediate value.
  useEffect(() => {
    setEventsLoading(true)
    setEventsError(null)
    const controller = new AbortController()
    const timer = setTimeout(() => {
      queryEvents(filters, { signal: controller.signal })
        .then((res) => {
          setEvents(res.events)
          setTotalCount(res.totalCount)
          setEventsLoading(false)
        })
        .catch((err: unknown) => {
          if (err instanceof DOMException && err.name === 'AbortError') return
          setEventsError(err instanceof ApiError ? err.message : 'Unbekannter Fehler.')
          setEventsLoading(false)
        })
    }, 300)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters, eventsReloadToken])

  // Sources are fetched once (independent of search filters): the event
  // list needs the org lookup regardless of which tab is active, and the
  // /sources tab reuses the same data.
  useEffect(() => {
    setSourcesLoading(true)
    setSourcesError(null)
    const controller = new AbortController()
    getSources(controller.signal)
      .then((res) => {
        setSources(res)
        setSourcesLoading(false)
      })
      .catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return
        setSourcesError(err instanceof ApiError ? err.message : 'Unbekannter Fehler.')
        setSourcesLoading(false)
      })
    return () => controller.abort()
  }, [sourcesReloadToken])

  const sourceOrgById = useMemo(() => {
    const map = new Map<string, string>()
    for (const source of sources ?? []) map.set(source.id, source.org)
    return map
  }, [sources])

  function sourceOrgOf(sourceId: string): string {
    return sourceOrgById.get(sourceId) ?? sourceId
  }

  return (
    <div className="app">
      <header className="topbar">
        <h1>EventFinder</h1>
        <p className="tagline">
          Tech-Meetups im Umkreis{filters.placeName ? ` von ${filters.placeName}` : ''}
          {!eventsLoading && !eventsError ? ` · ${formatCount(totalCount)} gefunden` : ''}
        </p>
        <nav className="tabs">
          <button type="button" className={view === 'search' ? 'tab-active' : ''} onClick={() => setView('search')}>
            Suche
          </button>
          <button type="button" className={view === 'sources' ? 'tab-active' : ''} onClick={() => setView('sources')}>
            Quellen
          </button>
        </nav>
      </header>

      {view === 'search' && (
        <div className="controls">
          <HomeLocationPicker placeName={filters.placeName} onSelect={pickPlace} />
          <FilterPanel filters={filters} onChange={patchFilters} />
          <SubscribeButton filters={filters} />
        </div>
      )}

      {view === 'search' && (
        <div className="split-view">
          <MapView
            events={events}
            homeLat={filters.lat}
            homeLon={filters.lon}
            radiusKm={filters.radiusKm}
            selectedId={selectedId}
            hoveredId={hoveredId}
            onSelect={setSelectedId}
            onHover={setHoveredId}
          />
          <div className="results-pane">
            <div className="results-search">
              <input
                id="search"
                type="search"
                placeholder="Veranstaltungen durchsuchen…"
                aria-label="Veranstaltungen durchsuchen"
                value={filters.search}
                onChange={(e) => patchFilters({ search: e.target.value })}
              />
            </div>
            <EventList
              events={events}
              totalCount={totalCount}
              loading={eventsLoading}
              error={eventsError}
              onRetry={() => setEventsReloadToken((t) => t + 1)}
              sourceOrgOf={sourceOrgOf}
              selectedId={selectedId}
              onSelect={setSelectedId}
              onHover={setHoveredId}
            />
          </div>
        </div>
      )}

      {view === 'sources' && (
        <SourcesPanel
          sources={sources}
          loading={sourcesLoading}
          error={sourcesError}
          onRetry={() => setSourcesReloadToken((t) => t + 1)}
        />
      )}
    </div>
  )
}
