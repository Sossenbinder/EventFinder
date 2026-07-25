import { useEffect, useState } from 'react'
import { ApiError, getUnresolvedEvents } from '../api'
import { formatCount, formatEventDateTime } from '../format'
import type { SourceStatusDto } from '../types'

interface Props {
  sources: SourceStatusDto[] | null
  loading: boolean
  error: string | null
  onRetry: () => void
}

function formatTimestamp(iso: string | null): string {
  if (!iso) return 'nie'
  return `${formatEventDateTime(iso)} Uhr`
}

// The outline's transparency view: every source, last run, last success,
// event count and last error, plus the count of events whose location
// couldn't be resolved (kept, never dropped -- AGENTS.md) so coverage gaps
// stay visible.
export default function SourcesPanel({ sources, loading, error, onRetry }: Props) {
  const [unresolvedCount, setUnresolvedCount] = useState<number | null>(null)
  const [unresolvedError, setUnresolvedError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getUnresolvedEvents(controller.signal)
      .then((events) => setUnresolvedCount(events.length))
      .catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return
        setUnresolvedError(err instanceof ApiError ? err.message : 'Unbekannter Fehler.')
      })
    return () => controller.abort()
  }, [])

  return (
    <div className="sources-panel">
      <h2>Quellen</h2>
      <p className="sources-intro">
        Jede Quelle wird regelmäßig automatisch abgerufen. Ausfälle einzelner Quellen betreffen nie die anderen.
      </p>

      <div className="unresolved-banner">
        {unresolvedError && <span>Standort-Statistik konnte nicht geladen werden: {unresolvedError}</span>}
        {!unresolvedError && unresolvedCount === null && <span>Lade Standort-Statistik…</span>}
        {!unresolvedError && unresolvedCount !== null && (
          <span>
            <strong>{formatCount(unresolvedCount)}</strong> Veranstaltungen ohne auflösbaren Standort (nicht in der
            Umkreissuche enthalten).
          </span>
        )}
      </div>

      {loading && (
        <div className="event-list-state">
          <div className="spinner" aria-hidden="true" />
          <p>Lade Quellen…</p>
        </div>
      )}

      {!loading && error && (
        <div className="event-list-state event-list-error">
          <p>Quellen konnten nicht geladen werden.</p>
          <p className="event-list-error-detail">{error}</p>
          <button type="button" onClick={onRetry}>
            Erneut versuchen
          </button>
        </div>
      )}

      {!loading && !error && sources && sources.length === 0 && (
        <div className="event-list-state">
          <p>Keine Quellen registriert.</p>
        </div>
      )}

      {!loading && !error && sources && sources.length > 0 && (
        <div className="sources-table-wrap">
          <table className="sources-table">
            <thead>
              <tr>
                <th>Organisation</th>
                <th>Typ</th>
                <th>Aktiv</th>
                <th>Letzter Lauf</th>
                <th>Letzter Erfolg</th>
                <th>Events</th>
                <th>Letzter Fehler</th>
              </tr>
            </thead>
            <tbody>
              {sources.map((source) => (
                <tr key={source.id} className={source.lastError ? 'source-row-error' : undefined}>
                  <td>
                    <a href={source.url} target="_blank" rel="noreferrer noopener">
                      {source.org}
                    </a>
                  </td>
                  <td>{source.type}</td>
                  <td>{source.enabled ? 'Ja' : 'Nein'}</td>
                  <td>{formatTimestamp(source.lastRunUtc)}</td>
                  <td>{formatTimestamp(source.lastSuccessUtc)}</td>
                  <td>{formatCount(source.eventCount)}</td>
                  <td className="source-error-cell">{source.lastError ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
