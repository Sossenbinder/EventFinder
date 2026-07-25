import { formatAttendance, formatDistanceKm, formatEventDateTime } from '../format'
import type { EventDto } from '../types'

interface Props {
  event: EventDto
  sourceOrg: string
  selected: boolean
  onSelect: () => void
  onHoverStart: () => void
  onHoverEnd: () => void
}

export default function EventListItem({ event, sourceOrg, selected, onSelect, onHoverStart, onHoverEnd }: Props) {
  return (
    <li
      className={`event-item${selected ? ' event-item-selected' : ''}`}
      onMouseEnter={onHoverStart}
      onMouseLeave={onHoverEnd}
      onClick={onSelect}
    >
      <div className="event-item-header">
        <span className="event-title">{event.title}</span>
        <span className={`attendance-badge attendance-${event.attendance.toLowerCase()}`}>
          {formatAttendance(event.attendance)}
        </span>
      </div>
      <div className="event-meta">{formatEventDateTime(event.startUtc)} Uhr</div>
      <div className="event-meta">
        {event.city ?? 'Ort unbekannt'} · {formatDistanceKm(event.distanceKm)} entfernt
      </div>
      <div className="event-footer">
        <span className="event-source">{sourceOrg}</span>
        <a
          className="event-link"
          href={event.url}
          target="_blank"
          rel="noreferrer noopener"
          onClick={(e) => e.stopPropagation()}
        >
          Zur Veranstaltung ↗
        </a>
      </div>
    </li>
  )
}
