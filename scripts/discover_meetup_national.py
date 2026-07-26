#!/usr/bin/env python3
"""National discovery sweep: Meetup tech groups across Germany's major cities.

Same approach as discover_meetup_groups.py (allowed /find/ paths, every candidate verified by
parsing its own /events/ page) but nationwide — the first two sweeps only covered towns within
reach of Kirchheim, which left Hamburg, Köln and Leipzig at literally zero coverage.
"""
import json
import re
import time
import urllib.error
import urllib.request

UA = "Mozilla/5.0 (X11; Linux x86_64) EventFinder/0.1 (+personal event aggregator)"
DELAY = 0.4

CITIES = [
    "Berlin", "Hamburg", "M%C3%BCnchen", "K%C3%B6ln", "Frankfurt", "D%C3%BCsseldorf",
    "Leipzig", "Dresden", "Hannover", "N%C3%BCrnberg", "Bremen", "Essen", "Dortmund",
    "Bonn", "M%C3%BCnster", "Mannheim", "Freiburg", "Heidelberg", "Darmstadt", "Mainz",
    "Wiesbaden", "Kassel", "Bielefeld", "Braunschweig", "Kiel", "L%C3%BCbeck", "Rostock",
    "Magdeburg", "Halle", "Jena", "Erfurt", "Chemnitz", "Potsdam", "Aachen", "Wuppertal",
    "Duisburg", "Bochum", "Osnabr%C3%BCck", "Oldenburg", "Paderborn", "W%C3%BCrzburg",
    "Regensburg", "Augsburg", "Ingolstadt", "Saarbr%C3%BCcken", "Trier", "Koblenz",
    "G%C3%B6ttingen", "Konstanz", "Kaiserslautern",
]

KEYWORDS = [
    "technology", "software", "developer", "ai", "machine learning", "data", "cloud",
    "kubernetes", "devops", "security", "python", "javascript", "java", "dotnet", "rust",
    "golang", "web", "mobile", "gamedev", "agile", "open source",
]


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Language": "de,en"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read().decode("utf-8", "replace")


def next_data(html):
    m = re.search(r'<script id="__NEXT_DATA__" type="application/json">(.*?)</script>', html, re.S)
    return json.loads(m.group(1)) if m else None


candidates = {}
for city in CITIES:
    found_here = 0
    for kw in KEYWORDS:
        url = f"https://www.meetup.com/find/?location=de--{city}&source=GROUPS&keywords={kw.replace(' ', '%20')}"
        try:
            data = next_data(get(url))
        except Exception as exc:
            print(f"  ! {city}/{kw}: {exc}", flush=True)
            time.sleep(DELAY)
            continue
        state = (data or {}).get("props", {}).get("pageProps", {}).get("__APOLLO_STATE__", {})
        for key, val in state.items():
            if key.startswith("Group:") and val.get("link"):
                slug = val["link"].rstrip("/").rsplit("/", 1)[-1]
                if slug not in candidates:
                    found_here += 1
                candidates.setdefault(slug, {"name": val.get("name", ""), "city": val.get("city", "")})
        time.sleep(DELAY)
    print(f"[find] {city}: +{found_here} new, {len(candidates)} total", flush=True)

json.dump({k: v for k, v in candidates.items()}, open("candidates3_raw.json", "w"), ensure_ascii=False)

verified = []
for i, (slug, meta) in enumerate(sorted(candidates.items()), 1):
    try:
        state = (next_data(get(f"https://www.meetup.com/{slug}/events/")) or {}) \
            .get("props", {}).get("pageProps", {}).get("__APOLLO_STATE__", {})
    except Exception:
        time.sleep(DELAY)
        continue

    venues = {k: v for k, v in state.items() if k.startswith("Venue:")}
    events = [v for k, v in state.items() if k.startswith("Event:") and v.get("dateTime")]
    upcoming = [e for e in events if e["dateTime"] > "2026-07-26"]

    where, physical = "", 0
    for e in upcoming:
        if e.get("eventType") in ("PHYSICAL", "HYBRID"):
            physical += 1
            ref = (e.get("venue") or {}).get("__ref")
            if ref in venues and not where:
                where = venues[ref].get("city") or ""

    verified.append({
        "slug": slug,
        "name": meta["name"] or (state.get(f"Group:{slug}", {}) or {}).get("name", ""),
        "search_city": meta["city"], "venue_city": where,
        "upcoming": len(upcoming), "physical": physical,
        "titles": [e.get("title", "")[:60] for e in upcoming[:2]],
    })
    if i % 50 == 0:
        print(f"[verify] {i}/{len(candidates)}", flush=True)
    time.sleep(DELAY)

verified.sort(key=lambda r: -r["physical"])
json.dump(verified, open("candidates3.json", "w"), ensure_ascii=False, indent=1)
print(f"\nDONE: {len(candidates)} candidates, {len(verified)} reachable, "
      f"{sum(1 for v in verified if v['upcoming'])} with upcoming events")
