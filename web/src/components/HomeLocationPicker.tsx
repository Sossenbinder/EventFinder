import { useEffect, useRef, useState } from 'react'
import { ApiError, searchPlaces } from '../api'
import type { PlaceDto } from '../types'

interface Props {
  placeName: string
  onSelect: (place: PlaceDto) => void
}

// Text input with autocomplete backed by GET /api/places?q=. Debounced so
// every keystroke doesn't fire a request; the query itself is otherwise
// unfiltered on the client — the gazetteer already ranks by population.
export default function HomeLocationPicker({ placeName, onSelect }: Props) {
  const [text, setText] = useState(placeName)
  const [results, setResults] = useState<PlaceDto[]>([])
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  // Keep the input in sync when the home location changes from elsewhere
  // (e.g. reading it back out of the URL on load).
  useEffect(() => setText(placeName), [placeName])

  useEffect(() => {
    if (!open) return
    const query = text.trim()
    if (query.length < 2) {
      setResults([])
      setLoading(false)
      setError(null)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setError(null)
    const timer = setTimeout(() => {
      searchPlaces(query, controller.signal)
        .then((places) => {
          setResults(places)
          setLoading(false)
        })
        .catch((err: unknown) => {
          if (err instanceof DOMException && err.name === 'AbortError') return
          setError(err instanceof ApiError ? err.message : 'Orte konnten nicht geladen werden.')
          setLoading(false)
        })
    }, 250)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [text, open])

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [])

  function pick(place: PlaceDto) {
    setText(place.name)
    setOpen(false)
    onSelect(place)
  }

  return (
    <div className="location-picker" ref={containerRef}>
      <label htmlFor="home-location">Standort</label>
      <input
        id="home-location"
        type="text"
        value={text}
        onChange={(e) => {
          setText(e.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        placeholder="Ort oder PLZ eingeben…"
        autoComplete="off"
      />
      {open && text.trim().length >= 2 && (
        <ul className="location-results">
          {loading && <li className="location-hint">Suche…</li>}
          {!loading && error && <li className="location-hint location-error">{error}</li>}
          {!loading && !error && results.length === 0 && <li className="location-hint">Keine Orte gefunden.</li>}
          {!loading &&
            !error &&
            results.map((place) => (
              <li key={`${place.name}-${place.latitude}-${place.longitude}`}>
                <button type="button" onClick={() => pick(place)}>
                  <span className="location-name">{place.name}</span>
                  <span className="location-admin">{place.admin1}</span>
                </button>
              </li>
            ))}
        </ul>
      )}
    </div>
  )
}
