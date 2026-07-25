import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { useEffect, useRef } from 'react'
import type { EventDto } from '../types'

interface Props {
  events: EventDto[]
  homeLat: number
  homeLon: number
  radiusKm: number
  selectedId: string | null
  hoveredId: string | null
  onSelect: (id: string | null) => void
  onHover: (id: string | null) => void
}

const RADIUS_SOURCE = 'home-radius'
const RADIUS_FILL_LAYER = 'home-radius-fill'
const RADIUS_LINE_LAYER = 'home-radius-line'
const EARTH_RADIUS_KM = 6371.0088

// Great-circle destination point, radians in, degrees out. Used to draw the
// radius circle client-side (no turf/geo dependency needed for one shape).
function destinationPoint(lat: number, lon: number, bearingDeg: number, distanceKm: number): [number, number] {
  const angularDistance = distanceKm / EARTH_RADIUS_KM
  const lat1 = (lat * Math.PI) / 180
  const lon1 = (lon * Math.PI) / 180
  const bearing = (bearingDeg * Math.PI) / 180

  const lat2 = Math.asin(
    Math.sin(lat1) * Math.cos(angularDistance) + Math.cos(lat1) * Math.sin(angularDistance) * Math.cos(bearing),
  )
  const lon2 =
    lon1 +
    Math.atan2(
      Math.sin(bearing) * Math.sin(angularDistance) * Math.cos(lat1),
      Math.cos(angularDistance) - Math.sin(lat1) * Math.sin(lat2),
    )

  return [(lon2 * 180) / Math.PI, (lat2 * 180) / Math.PI]
}

function circlePolygon(lat: number, lon: number, radiusKm: number): GeoJSON.Feature<GeoJSON.Polygon> {
  const steps = 72
  const ring: [number, number][] = []
  for (let i = 0; i <= steps; i++) {
    ring.push(destinationPoint(lat, lon, (360 / steps) * i, radiusKm))
  }
  return {
    type: 'Feature',
    properties: {},
    geometry: { type: 'Polygon', coordinates: [ring] },
  }
}

// Bounding box of the radius circle, so the map can frame the whole search
// area. Without this the map sits at a fixed zoom and a 75 km circle runs off
// the edges -- the radius is drawn but its outline is never visible.
function radiusBounds(lat: number, lon: number, radiusKm: number): [[number, number], [number, number]] {
  const [, north] = destinationPoint(lat, lon, 0, radiusKm)
  const [east] = destinationPoint(lat, lon, 90, radiusKm)
  const [, south] = destinationPoint(lat, lon, 180, radiusKm)
  const [west] = destinationPoint(lat, lon, 270, radiusKm)
  return [
    [west, south],
    [east, north],
  ]
}

export default function MapView({ events, homeLat, homeLon, radiusKm, selectedId, hoveredId, onSelect, onHover }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const loadedRef = useRef(false)
  const homeMarkerRef = useRef<maplibregl.Marker | null>(null)
  const eventMarkersRef = useRef<Map<string, maplibregl.Marker>>(new Map())
  const onSelectRef = useRef(onSelect)
  onSelectRef.current = onSelect
  const onHoverRef = useRef(onHover)
  onHoverRef.current = onHover

  // Map instance: created once. OpenFreeMap's "positron" style is free,
  // requires no API key and needs only the attribution below (see
  // web/README.md) -- matches Mietmap/web's basemap choice.
  useEffect(() => {
    const map = new maplibregl.Map({
      container: containerRef.current!,
      style: 'https://tiles.openfreemap.org/styles/positron',
      center: [homeLon, homeLat],
      zoom: 9,
      attributionControl: false,
    })
    mapRef.current = map

    map.addControl(
      new maplibregl.AttributionControl({
        compact: true,
        customAttribution: 'Karte: © <a href="https://openfreemap.org">OpenFreeMap</a>, Daten © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>-Mitwirkende',
      }),
    )
    map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'bottom-right')

    map.on('load', () => {
      map.addSource(RADIUS_SOURCE, { type: 'geojson', data: circlePolygon(homeLat, homeLon, radiusKm) })
      map.addLayer({
        id: RADIUS_FILL_LAYER,
        type: 'fill',
        source: RADIUS_SOURCE,
        paint: { 'fill-color': '#1d4ed8', 'fill-opacity': 0.06 },
      })
      map.addLayer({
        id: RADIUS_LINE_LAYER,
        type: 'line',
        source: RADIUS_SOURCE,
        paint: { 'line-color': '#1d4ed8', 'line-width': 1.5, 'line-dasharray': [2, 2] },
      })
      map.fitBounds(radiusBounds(homeLat, homeLon, radiusKm), { padding: 32, duration: 0 })
      loadedRef.current = true
    })

    // The map shares its row with the result list, so its container is sized by
    // layout rather than by the window. Without this the transform keeps the
    // width it had at construction time and every marker is projected to the
    // wrong pixel -- the home pin lands tens of kilometres east of the town it
    // is supposed to mark.
    const observer = new ResizeObserver(() => map.resize())
    observer.observe(containerRef.current!)

    return () => {
      observer.disconnect()
      map.remove()
      mapRef.current = null
      loadedRef.current = false
      homeMarkerRef.current = null
      eventMarkersRef.current.clear()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Home marker + recentering: the outline requires that picking a place
  // recenters the map.
  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    if (!homeMarkerRef.current) {
      const el = document.createElement('div')
      el.className = 'home-marker'
      homeMarkerRef.current = new maplibregl.Marker({ element: el, anchor: 'center' })
        .setLngLat([homeLon, homeLat])
        .addTo(map)
    } else {
      homeMarkerRef.current.setLngLat([homeLon, homeLat])
    }

  }, [homeLat, homeLon])

  // Radius circle: recomputed whenever the center or radius changes, and the
  // viewport follows it so the drawn circle stays framed.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !loadedRef.current) return
    const source = map.getSource(RADIUS_SOURCE) as maplibregl.GeoJSONSource | undefined
    source?.setData(circlePolygon(homeLat, homeLon, radiusKm))
    map.fitBounds(radiusBounds(homeLat, homeLon, radiusKm), { padding: 32, duration: 800 })
  }, [homeLat, homeLon, radiusKm])

  // Event markers: the result set changes on every filter edit, and events
  // are few enough (curated registry, not a platform-wide crawl) that
  // rebuilding the marker set from scratch on each change is simpler than
  // diffing, and cheap enough not to matter.
  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    for (const marker of eventMarkersRef.current.values()) marker.remove()
    eventMarkersRef.current.clear()

    for (const event of events) {
      const el = document.createElement('div')
      el.className = 'event-marker'
      el.addEventListener('click', (e) => {
        e.stopPropagation()
        onSelectRef.current(event.id)
      })
      el.addEventListener('mouseenter', () => onHoverRef.current(event.id))
      el.addEventListener('mouseleave', () => onHoverRef.current(null))

      const marker = new maplibregl.Marker({ element: el, anchor: 'bottom' })
        .setLngLat([event.longitude, event.latitude])
        .addTo(map)
      eventMarkersRef.current.set(event.id, marker)
    }

    return () => {
      for (const marker of eventMarkersRef.current.values()) marker.remove()
      eventMarkersRef.current.clear()
    }
  }, [events])

  // Highlight whichever marker is selected or hovered, from either
  // direction (list -> map and map -> list share the same state in App.tsx).
  useEffect(() => {
    for (const [id, marker] of eventMarkersRef.current.entries()) {
      const el = marker.getElement()
      el.classList.toggle('event-marker-active', id === selectedId || id === hoveredId)
    }
  }, [selectedId, hoveredId])

  return <div ref={containerRef} className="map" />
}
