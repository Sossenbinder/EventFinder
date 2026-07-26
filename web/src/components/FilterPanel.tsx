import { useState } from 'react'
import { MAX_RADIUS_KM, MIN_RADIUS_KM } from '../urlState'
import type { Attendance, Filters } from '../types'

interface Props {
  filters: Filters
  onChange: (patch: Partial<Filters>) => void
}

const ATTENDANCE_OPTIONS: { value: Attendance | ''; label: string }[] = [
  { value: '', label: 'Alle' },
  { value: 'InPerson', label: 'Vor Ort' },
  { value: 'Online', label: 'Online' },
  { value: 'Hybrid', label: 'Hybrid' },
]

export default function FilterPanel({ filters, onChange }: Props) {
  const [tagInput, setTagInput] = useState('')

  function addTag(raw: string) {
    const tag = raw.trim().toLowerCase()
    if (!tag || filters.tags.includes(tag)) {
      setTagInput('')
      return
    }
    onChange({ tags: [...filters.tags, tag] })
    setTagInput('')
  }

  function removeTag(tag: string) {
    onChange({ tags: filters.tags.filter((t) => t !== tag) })
  }

  return (
    <div className="filter-panel">
      <div className="filter-row filter-row-search">
        <label htmlFor="search">Suche</label>
        <input
          id="search"
          type="search"
          placeholder="Titel, Ort, Veranstalter…"
          value={filters.search}
          onChange={(e) => onChange({ search: e.target.value })}
        />
      </div>

      <div className="filter-row">
        <label htmlFor="radius">
          Umkreis: <strong>{filters.radiusKm} km</strong>
        </label>
        <input
          id="radius"
          type="range"
          min={MIN_RADIUS_KM}
          max={MAX_RADIUS_KM}
          step={5}
          value={filters.radiusKm}
          onChange={(e) => onChange({ radiusKm: Number(e.target.value) })}
        />
      </div>

      <div className="filter-row filter-row-dates">
        <div>
          <label htmlFor="from">Von</label>
          <input
            id="from"
            type="date"
            value={filters.from ?? ''}
            onChange={(e) => onChange({ from: e.target.value || null })}
          />
        </div>
        <div>
          <label htmlFor="to">Bis</label>
          <input
            id="to"
            type="date"
            value={filters.to ?? ''}
            onChange={(e) => onChange({ to: e.target.value || null })}
          />
        </div>
      </div>

      <div className="filter-row">
        <label htmlFor="attendance">Teilnahme</label>
        <select
          id="attendance"
          value={filters.attendance ?? ''}
          onChange={(e) => onChange({ attendance: (e.target.value || null) as Attendance | null })}
        >
          {ATTENDANCE_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
      </div>

      <div className="filter-row">
        <label htmlFor="tags">Themen</label>
        <div className="tag-input">
          <input
            id="tags"
            type="text"
            value={tagInput}
            onChange={(e) => setTagInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ',') {
                e.preventDefault()
                addTag(tagInput)
              }
            }}
            onBlur={() => addTag(tagInput)}
            placeholder="z. B. java, python…"
          />
          {filters.tags.length > 0 && (
            <div className="tag-chips">
              {filters.tags.map((tag) => (
                <span className="tag-chip" key={tag}>
                  {tag}
                  <button type="button" aria-label={`Tag ${tag} entfernen`} onClick={() => removeTag(tag)}>
                    ×
                  </button>
                </span>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
