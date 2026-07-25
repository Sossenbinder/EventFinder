import { formatCount } from '../format'
import type { EventDto } from '../types'
import EventListItem from './EventListItem'

interface Props {
  events: EventDto[]
  totalCount: number
  loading: boolean
  error: string | null
  onRetry: () => void
  sourceOrgOf: (sourceId: string) => string
  selectedId: string | null
  onSelect: (id: string | null) => void
  onHover: (id: string | null) => void
}

// Ordered by start date already (GET /api/events sorts server-side), so this
// component just renders what it's given.
export default function EventList({
  events,
  totalCount,
  loading,
  error,
  onRetry,
  sourceOrgOf,
  selectedId,
  onSelect,
  onHover,
}: Props) {
  if (loading) {
    return (
      <div className="event-list-state">
        <div className="spinner" aria-hidden="true" />
        <p>Lade Veranstaltungen…</p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="event-list-state event-list-error">
        <p>Veranstaltungen konnten nicht geladen werden.</p>
        <p className="event-list-error-detail">{error}</p>
        <button type="button" onClick={onRetry}>
          Erneut versuchen
        </button>
      </div>
    )
  }

  if (events.length === 0) {
    return (
      <div className="event-list-state">
        <p>Keine Veranstaltungen im gewählten Umkreis und Zeitraum gefunden.</p>
        <p className="event-list-hint">Versuch es mit einem größeren Umkreis oder weniger Filtern.</p>
      </div>
    )
  }

  return (
    <div className="event-list">
      <p className="event-list-count">{formatCount(totalCount)} Veranstaltungen gefunden</p>
      <ul>
        {events.map((event) => (
          <EventListItem
            key={event.id}
            event={event}
            sourceOrg={sourceOrgOf(event.sourceId)}
            selected={event.id === selectedId}
            onSelect={() => onSelect(event.id === selectedId ? null : event.id)}
            onHoverStart={() => onHover(event.id)}
            onHoverEnd={() => onHover(null)}
          />
        ))}
      </ul>
    </div>
  )
}
