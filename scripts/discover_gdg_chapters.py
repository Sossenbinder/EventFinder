#!/usr/bin/env python3
"""Find every German GDG/Bevy chapter that has upcoming events.

The registry's 25 chapter slugs came from the /api/ chapter search, which is robots-disallowed
AND caps at 1000 alphabetically-ordered results — so chapters late in the alphabet were invisible.
This walks the published sitemaps instead (allowed), takes the chapter slug out of each future
event URL, and checks each chapter page's embedded "country" field.

Honours robots.txt Crawl-delay: 2 on gdg.community.dev.
"""
import json
import re
import time
import urllib.request

UA = "EventFinder/0.1 (+personal event aggregator)"
CRAWL_DELAY = 2.0
BASE = "https://gdg.community.dev"


def get(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=40) as r:
        return r.read().decode("utf-8", "replace")


index = get(f"{BASE}/sitemap.xml")
time.sleep(CRAWL_DELAY)
months = [
    loc for loc in re.findall(r"<loc>([^<]+)</loc>", index)
    if (m := re.search(r"sitemap-events-(\d{4})-(\d{2})\.xml", loc)) and f"{m.group(1)}-{m.group(2)}" >= "2026-07"
]
print(f"future event sitemaps: {len(months)}", flush=True)

slugs = {}
for loc in months:
    try:
        xml = get(loc)
    except Exception as exc:
        print(f"  ! {loc}: {exc}", flush=True)
        time.sleep(CRAWL_DELAY)
        continue
    for url in re.findall(r"<loc>([^<]+)</loc>", xml):
        m = re.search(r"/events/details/google-(.+?)-presents-", url)
        if m:
            slugs.setdefault(m.group(1), 0)
            slugs[m.group(1)] += 1
    time.sleep(CRAWL_DELAY)

print(f"distinct chapter slugs with future events: {len(slugs)}", flush=True)

german = []
for i, slug in enumerate(sorted(slugs), 1):
    try:
        html = get(f"{BASE}/{slug}/")
    except Exception:
        time.sleep(CRAWL_DELAY)
        continue
    country = re.search(r'"country"\s*:\s*"([^"]+)"', html)
    if country and country.group(1) == "DE":
        loc = re.search(r'"chapter_location"\s*:\s*"([^"]*)"', html)
        title = re.search(r'"chapter_title"\s*:\s*"([^"]*)"', html)
        german.append({
            "slug": slug,
            "title": title.group(1) if title else slug,
            "location": loc.group(1) if loc else "",
            "future_events": slugs[slug],
        })
        print(f"  DE  {slug:<52} {german[-1]['title'][:34]}  ({slugs[slug]} events)", flush=True)
    if i % 50 == 0:
        print(f"[{i}/{len(slugs)}] german so far: {len(german)}", flush=True)
    time.sleep(CRAWL_DELAY)

json.dump(german, open("gdg_german_chapters.json", "w"), ensure_ascii=False, indent=1)
print(f"\nDONE: {len(german)} German chapters with upcoming events "
      f"(registry currently has 25)")
