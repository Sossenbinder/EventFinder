import type { Attendance, Filters } from './types'

// Kirchheim unter Teck — the outline's default home location (this is where
// the "everything looks like it happens in Stuttgart" problem starts).
export const DEFAULT_LAT = 48.6468
export const DEFAULT_LON = 9.4538
export const DEFAULT_PLACE_NAME = 'Kirchheim unter Teck'
export const DEFAULT_RADIUS_KM = 50
export const MIN_RADIUS_KM = 10
export const MAX_RADIUS_KM = 200

export type View = 'search' | 'sources'

const ATTENDANCE_VALUES: readonly Attendance[] = ['InPerson', 'Online', 'Hybrid']

function isAttendance(value: string): value is Attendance {
  return (ATTENDANCE_VALUES as readonly string[]).includes(value)
}

export const DEFAULT_FILTERS: Filters = {
  lat: DEFAULT_LAT,
  lon: DEFAULT_LON,
  placeName: DEFAULT_PLACE_NAME,
  radiusKm: DEFAULT_RADIUS_KM,
  from: null,
  to: null,
  tags: [],
  attendance: null,
}

// All filters (and the active tab) live in the URL query string so a search
// can be bookmarked or shared, per the outline's "deep-linkable state"
// requirement.
export function parseStateFromUrl(search: string): { filters: Filters; view: View } {
  const params = new URLSearchParams(search)

  const lat = Number(params.get('lat'))
  const lon = Number(params.get('lon'))
  const radiusKm = Number(params.get('radiusKm'))
  const attendanceParam = params.get('attendance')
  const view = params.get('view')

  const filters: Filters = {
    lat: Number.isFinite(lat) && params.has('lat') ? lat : DEFAULT_FILTERS.lat,
    lon: Number.isFinite(lon) && params.has('lon') ? lon : DEFAULT_FILTERS.lon,
    placeName: params.get('place') ?? DEFAULT_FILTERS.placeName,
    radiusKm:
      Number.isFinite(radiusKm) && radiusKm >= MIN_RADIUS_KM && radiusKm <= MAX_RADIUS_KM
        ? radiusKm
        : DEFAULT_FILTERS.radiusKm,
    from: params.get('from'),
    to: params.get('to'),
    tags: params.get('tags')?.split(',').filter(Boolean) ?? [],
    attendance: attendanceParam && isAttendance(attendanceParam) ? attendanceParam : null,
  }

  return {
    filters,
    view: view === 'sources' ? 'sources' : 'search',
  }
}

export function stateToUrl(filters: Filters, view: View): string {
  const params = new URLSearchParams()
  params.set('lat', filters.lat.toFixed(5))
  params.set('lon', filters.lon.toFixed(5))
  params.set('place', filters.placeName)
  params.set('radiusKm', String(filters.radiusKm))
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  if (filters.tags.length > 0) params.set('tags', filters.tags.join(','))
  if (filters.attendance) params.set('attendance', filters.attendance)
  if (view === 'sources') params.set('view', view)
  return `${window.location.pathname}?${params.toString()}`
}
