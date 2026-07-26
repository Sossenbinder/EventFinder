import type { EventsResponse, Filters, PlaceDto, SourceStatusDto, UnresolvedEventDto } from './types'

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

const MAX_ERROR_BODY_LENGTH = 300

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(path, { signal })
  if (!res.ok) {
    let body = await res.text().catch(() => '')
    // The dev-time ASP.NET Core exception page returns a full stack trace as
    // plain text; truncate so a backend error can't turn this into an
    // unreadable wall of text in the UI.
    if (body.length > MAX_ERROR_BODY_LENGTH) {
      body = `${body.slice(0, MAX_ERROR_BODY_LENGTH)}…`
    }
    throw new ApiError(`${res.status} ${res.statusText}${body ? `: ${body}` : ''}`, res.status)
  }
  return (await res.json()) as T
}

// Shared by GET /api/events and GET /api/events.ics (EventQueryParsing.cs
// binds both from the same set of query params), so the map/list query and
// the subscribe-link URL can never drift apart.
export function buildEventQueryParams(filters: Filters, extra?: { limit?: number; offset?: number }): URLSearchParams {
  const params = new URLSearchParams()
  params.set('lat', String(filters.lat))
  params.set('lon', String(filters.lon))
  params.set('radiusKm', String(filters.radiusKm))
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  for (const tag of filters.tags) params.append('tags', tag)
  if (filters.attendance) params.set('attendance', filters.attendance)
  if (filters.search.trim()) params.set('q', filters.search.trim())
  if (extra?.limit !== undefined) params.set('limit', String(extra.limit))
  if (extra?.offset !== undefined) params.set('offset', String(extra.offset))
  return params
}

export async function queryEvents(
  filters: Filters,
  options?: { limit?: number; offset?: number; signal?: AbortSignal },
): Promise<EventsResponse> {
  const params = buildEventQueryParams(filters, options)
  return getJson<EventsResponse>(`/api/events?${params.toString()}`, options?.signal)
}

export async function searchPlaces(query: string, signal?: AbortSignal): Promise<PlaceDto[]> {
  if (!query.trim()) return []
  const params = new URLSearchParams({ q: query })
  return getJson<PlaceDto[]>(`/api/places?${params.toString()}`, signal)
}

export async function getUnresolvedEvents(signal?: AbortSignal): Promise<UnresolvedEventDto[]> {
  return getJson<UnresolvedEventDto[]>('/api/events/unresolved', signal)
}

export async function getSources(signal?: AbortSignal): Promise<SourceStatusDto[]> {
  return getJson<SourceStatusDto[]>('/api/sources', signal)
}

// Absolute (not relative) so it can be copied into a calendar app running
// outside this page's origin. `window.location.origin` is correct both in
// dev (proxied through Vite to the API) and in production (the API serves
// this SPA's static files itself, same origin).
export function buildEventsIcsUrl(filters: Filters): string {
  const params = buildEventQueryParams(filters, { limit: 500 })
  return `${window.location.origin}/api/events.ics?${params.toString()}`
}

export function toWebcalUrl(httpUrl: string): string {
  return httpUrl.replace(/^https?:\/\//, 'webcal://')
}
