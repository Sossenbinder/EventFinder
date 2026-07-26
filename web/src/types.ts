// Mirrors the API's DTOs exactly (see src/EventFinder.Api/Endpoints/*.cs).
// System.Text.Json serializes with camelCase property names and
// JsonStringEnumConverter (Program.cs), so Attendance comes across as one of
// the three string literals below rather than a number.

export type Attendance = 'InPerson' | 'Online' | 'Hybrid'

export interface EventDto {
  id: string
  sourceId: string
  title: string
  description: string | null
  startUtc: string
  endUtc: string | null
  timeZoneId: string
  venueName: string | null
  venueAddress: string | null
  city: string | null
  postalCode: string | null
  latitude: number
  longitude: number
  distanceKm: number
  attendance: Attendance
  url: string
  tags: string[]
}

export interface EventsResponse {
  events: EventDto[]
  totalCount: number
}

export interface UnresolvedEventDto {
  id: string
  sourceId: string
  title: string
  startUtc: string
  venueName: string | null
  venueAddress: string | null
  city: string | null
  postalCode: string | null
  attendance: Attendance
  url: string
}

export interface PlaceDto {
  name: string
  admin1: string
  population: number
  latitude: number
  longitude: number
}

export interface SourceStatusDto {
  id: string
  org: string
  type: string
  url: string
  enabled: boolean
  lastRunUtc: string | null
  lastSuccessUtc: string | null
  eventCount: number
  lastError: string | null
}

// Client-side filter state; the single source of truth for both the /api
// query params and the URL query string (see urlState.ts).
export interface Filters {
  lat: number
  lon: number
  placeName: string
  radiusKm: number
  from: string | null // yyyy-MM-dd
  to: string | null // yyyy-MM-dd
  tags: string[]
  attendance: Attendance | null
  search: string
}
