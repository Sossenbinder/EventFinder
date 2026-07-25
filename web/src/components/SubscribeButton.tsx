import { useState } from 'react'
import { buildEventsIcsUrl, toWebcalUrl } from '../api'
import type { Filters } from '../types'

interface Props {
  filters: Filters
}

// Hands the user the /api/events.ics URL for their current filters so they
// can subscribe to their radius search in a calendar app (outline: "GET
// /api/events.ics -- subscribe to your filtered radius as a calendar feed").
export default function SubscribeButton({ filters }: Props) {
  const [open, setOpen] = useState(false)
  const [copied, setCopied] = useState(false)

  const icsUrl = buildEventsIcsUrl(filters)
  const webcalUrl = toWebcalUrl(icsUrl)

  async function copy() {
    try {
      await navigator.clipboard.writeText(icsUrl)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API can be unavailable (permissions, non-HTTPS context);
      // the URL is still shown in the text field for manual copy.
    }
  }

  return (
    <div className="subscribe">
      <button type="button" className="subscribe-toggle" onClick={() => setOpen((v) => !v)}>
        📅 Kalender abonnieren
      </button>
      {open && (
        <div className="subscribe-panel">
          <p>Abonniere diese Suche (Umkreis, Zeitraum und Filter) als Kalender-Feed:</p>
          <div className="subscribe-url-row">
            <input type="text" readOnly value={icsUrl} onFocus={(e) => e.currentTarget.select()} />
            <button type="button" onClick={copy}>
              {copied ? 'Kopiert ✓' : 'Kopieren'}
            </button>
          </div>
          <a className="subscribe-webcal-link" href={webcalUrl}>
            In Kalender-App öffnen (webcal://)
          </a>
          <p className="subscribe-hint">
            Der Feed aktualisiert sich automatisch, sobald deine Kalender-App neu synchronisiert.
          </p>
        </div>
      )}
    </div>
  )
}
