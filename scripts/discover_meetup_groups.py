#!/usr/bin/env python3
"""Wider one-off discovery sweep for Meetup groups in reach of Kirchheim unter Teck.

Curation input, not a shipped crawler. Two harvesting routes, both on paths meetup.com's
robots.txt allows (/find/ and /cities/ are not disallowed):
  1. /find/?location=de--<city>&source=GROUPS&keywords=<kw>  -> up to 15 groups per query
  2. /cities/de/<city>/ -> group slugs extracted from the events it features

Every candidate is then verified by parsing its own /events/ page, so nothing unverified can
reach sources.yaml. Output: candidates2.json for human curation.
"""
import json
import re
import time
import urllib.error
import urllib.request

UA = "Mozilla/5.0 (X11; Linux x86_64) EventFinder/0.1 (+personal event aggregator)"
DELAY = 0.6

# Towns within realistic reach of Kirchheim unter Teck, plus the regional hubs.
CITIES = [
    "Stuttgart", "Esslingen", "G%C3%B6ppingen", "N%C3%BCrtingen", "B%C3%B6blingen",
    "Sindelfingen", "Ludwigsburg", "Waiblingen", "Reutlingen", "T%C3%BCbingen",
    "Ulm", "Heilbronn", "Karlsruhe", "Pforzheim", "Aalen", "Schw%C3%A4bisch+Gm%C3%BCnd",
]

KEYWORDS = [
    "technology", "software", "developer", "programming", "ai", "machine learning",
    "data", "data science", "cloud", "kubernetes", "devops", "security", "linux",
    "open source", "python", "javascript", "typescript", "java", "dotnet", "rust",
    "golang", "php", "web", "mobile", "gamedev", "ux", "testing", "architecture",
    "iot", "embedded", "robotics", "startup", "agile", "blockchain",
]

# From Meetup's own city sitemap (sw_cities_1.xml.gz), filtered to our region.
CITY_PAGES = [
    "stuttgart", "karlsruhe", "heilbronn", "ulm", "pforzheim", "reutlingen",
    "esslingen", "ludwigsburg", "aalen", "sindelfingen", "neu-ulm", "kirchheim",
]


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Language": "de,en"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read().decode("utf-8", "replace")


def next_data(html):
    m = re.search(r'<script id="__NEXT_DATA__" type="application/json">(.*?)</script>', html, re.S)
    return json.loads(m.group(1)) if m else None


candidates = {}
totals = {}

for city in CITIES:
    for kw in KEYWORDS:
        url = f"https://www.meetup.com/find/?location=de--{city}&source=GROUPS&keywords={kw.replace(' ', '%20')}"
        try:
            data = next_data(get(url))
        except Exception as exc:
            print(f"  ! find {city}/{kw}: {exc}", flush=True)
            time.sleep(DELAY)
            continue
        state = (data or {}).get("props", {}).get("pageProps", {}).get("__APOLLO_STATE__", {})
        for key, val in state.items():
            if key.startswith("Group:") and val.get("link"):
                slug = val["link"].rstrip("/").rsplit("/", 1)[-1]
                candidates.setdefault(slug, {"name": val.get("name", ""), "city": val.get("city", ""), "via": set()})
                candidates[slug]["via"].add(f"{city}/{kw}")
        for key, val in state.get("ROOT_QUERY", {}).items():
            if key.startswith("groupSearch") and isinstance(val, dict):
                totals[f"{city}/{kw}"] = val.get("totalCount")
        time.sleep(DELAY)
    print(f"[find] {city}: running total {len(candidates)} candidates", flush=True)

for city in CITY_PAGES:
    try:
        data = next_data(get(f"https://www.meetup.com/cities/de/{city}/"))
    except Exception as exc:
        print(f"  ! city {city}: {exc}", flush=True)
        time.sleep(DELAY)
        continue
    pp = (data or {}).get("props", {}).get("pageProps", {})
    for bucket in ("eventsInLocation", "todayEvents", "thisWeekendEvents"):
        for ev in pp.get(bucket) or []:
            m = re.match(r"https://www\.meetup\.com/([^/]+)/events/", ev.get("eventUrl", ""))
            if m:
                slug = m.group(1)
                candidates.setdefault(slug, {"name": "", "city": city, "via": set()})
                candidates[slug]["via"].add(f"cities/{city}")
    time.sleep(DELAY)
print(f"[cities] total candidates: {len(candidates)}", flush=True)

verified = []
for i, (slug, meta) in enumerate(sorted(candidates.items()), 1):
    try:
        data = next_data(get(f"https://www.meetup.com/{slug}/events/"))
    except urllib.error.HTTPError as exc:
        print(f"  {slug:<46} HTTP {exc.code}", flush=True)
        time.sleep(DELAY)
        continue
    except Exception as exc:
        print(f"  {slug:<46} ERR {exc}", flush=True)
        time.sleep(DELAY)
        continue

    state = (data or {}).get("props", {}).get("pageProps", {}).get("__APOLLO_STATE__", {})
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

    name = meta["name"] or (state.get(f"Group:{slug}", {}) or {}).get("name", "")
    verified.append({
        "slug": slug, "name": name, "search_city": meta["city"], "venue_city": where,
        "upcoming": len(upcoming), "physical": physical,
        "next": upcoming[0]["dateTime"][:10] if upcoming else None,
        "titles": [e.get("title", "")[:70] for e in upcoming[:3]],
        "via": sorted(meta["via"])[:3],
    })
    if i % 25 == 0:
        print(f"[verify] {i}/{len(candidates)}", flush=True)
    time.sleep(DELAY)

verified.sort(key=lambda r: -r["upcoming"])
json.dump(verified, open("candidates2.json", "w"), ensure_ascii=False, indent=1)
live = [v for v in verified if v["upcoming"]]
print(f"\nDONE: {len(candidates)} candidates, {len(verified)} reachable, {len(live)} with upcoming events")
