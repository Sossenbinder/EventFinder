// German (de-DE) formatting helpers. All event timestamps come from the API
// as UTC ISO strings (see types.ts); the audience is in Germany, so every
// display renders them in Europe/Berlin regardless of the viewer's own
// system timezone.

const BERLIN_TZ = 'Europe/Berlin'

const dateTimeFormatter = new Intl.DateTimeFormat('de-DE', {
  timeZone: BERLIN_TZ,
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

const dateFormatter = new Intl.DateTimeFormat('de-DE', {
  timeZone: BERLIN_TZ,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
})

const distanceFormatter = new Intl.NumberFormat('de-DE', {
  maximumFractionDigits: 1,
})

const countFormatter = new Intl.NumberFormat('de-DE')

export function formatEventDateTime(iso: string): string {
  return dateTimeFormatter.format(new Date(iso))
}

export function formatDate(iso: string): string {
  return dateFormatter.format(new Date(iso))
}

export function formatDistanceKm(km: number): string {
  return `${distanceFormatter.format(km)} km`
}

export function formatCount(n: number): string {
  return countFormatter.format(n)
}

const ATTENDANCE_LABELS: Record<import('./types').Attendance, string> = {
  InPerson: 'Vor Ort',
  Online: 'Online',
  Hybrid: 'Hybrid',
}

export function formatAttendance(a: import('./types').Attendance): string {
  return ATTENDANCE_LABELS[a]
}
