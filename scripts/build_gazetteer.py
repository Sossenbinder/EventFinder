#!/usr/bin/env python3
"""Build EventFinder's offline German gazetteer from GeoNames dumps.

Inputs (downloaded from https://download.geonames.org, CC BY 4.0):
  dump/DE.txt                 - all features; we keep feature class P (populated places)
  dump/alternatenames/DE.txt  - alternate names *with language codes*
  zip/DE.txt                  - postal codes

Outputs (semicolon-separated, UTF-8, header row):
  places-de.csv  name;aliases;admin1;population;lat;lon
  postal-de.csv  plz;name;admin1;lat;lon

Why the alternate-names file is required: GeoNames' primary `name` column is inconsistent for
German cities. It holds the English exonym for Munich and Nuremberg but the German name for Köln.
Matching a German venue string against "Munich" fails, so the canonical name is taken from the
preferred German alternate name where one exists, and the GeoNames primary name is demoted to an
alias so English spellings still resolve.
"""
import csv
import sys
import unicodedata

DUMP, ALT, ZIP, OUT_PLACES, OUT_POSTAL = sys.argv[1:6]

ADMIN1 = {
    "01": "BW", "02": "BY", "03": "HB", "04": "HH", "05": "HE", "06": "NI",
    "07": "NW", "08": "RP", "09": "SL", "10": "SH", "11": "BB", "12": "MV",
    "13": "SN", "14": "ST", "15": "TH", "16": "BE",
}

# Aliases shorter than this are dropped: GeoNames carries airport/IATA-style codes such as "BER"
# for Berlin and "MUC" for Munich, which would match far too eagerly against address text.
MIN_ALIAS_LEN = 4
MAX_ALIASES = 5


def fold(s: str) -> str:
    """Casefold + strip diacritics, with German umlaut expansion."""
    s = s.lower()
    for a, b in (("ä", "ae"), ("ö", "oe"), ("ü", "ue"), ("ß", "ss")):
        s = s.replace(a, b)
    s = unicodedata.normalize("NFKD", s)
    return "".join(c for c in s if not unicodedata.combining(c))


def related(alias: str, name: str) -> bool:
    """Whether a language-untagged alternate name plausibly denotes the same German place.

    GeoNames ships transliterations into many scripts; romanised forms such as
    'Kirkhajm pod Tekom' pass a Latin-charset filter but would only create false positives when
    matching venue strings. Require a shared prefix or a containment relation.
    """
    a, n = fold(alias), fold(name)
    if a == n:
        return False
    return a in n or n in a or a[:4] == n[:4]


# geonameid -> German names, preferred spelling first.
german = {}
with open(ALT, encoding="utf-8") as fh:
    for row in csv.reader(fh, delimiter="\t", quoting=csv.QUOTE_NONE):
        if len(row) < 5 or row[2] != "de" or not row[3]:
            continue
        names = german.setdefault(int(row[1]), [])
        if row[4] == "1":
            names.insert(0, row[3])
        else:
            names.append(row[3])

places = {}
with open(DUMP, encoding="utf-8") as fh:
    for row in csv.reader(fh, delimiter="\t", quoting=csv.QUOTE_NONE):
        if len(row) < 15 or row[6] != "P":
            continue
        geonameid, geonames_name = int(row[0]), row[1]
        admin1, population = ADMIN1.get(row[10], ""), int(row[14] or 0)

        de_names = german.get(geonameid, [])
        name = de_names[0] if de_names else geonames_name

        aliases, seen = [], {fold(name)}
        candidates = de_names[1:] + [geonames_name, row[2]]
        candidates += [a for a in row[3].split(",") if related(a, name)]
        for candidate in candidates:
            key = fold(candidate)
            if len(candidate) >= MIN_ALIAS_LEN and key not in seen:
                seen.add(key)
                aliases.append(candidate)
            if len(aliases) == MAX_ALIASES:
                break

        # A name can repeat within a state; keep the most populous instance.
        key = (fold(name), admin1)
        if key not in places or population > places[key][3]:
            places[key] = (name, "|".join(aliases), admin1, population, row[4], row[5])

with open(OUT_PLACES, "w", encoding="utf-8", newline="") as fh:
    out = csv.writer(fh, delimiter=";", quoting=csv.QUOTE_MINIMAL)
    out.writerow(["name", "aliases", "admin1", "population", "lat", "lon"])
    for row in sorted(places.values(), key=lambda r: (-r[3], r[0])):
        out.writerow(row)

postal = {}
with open(ZIP, encoding="utf-8") as fh:
    for row in csv.reader(fh, delimiter="\t", quoting=csv.QUOTE_NONE):
        if len(row) < 11 or not row[1]:
            continue
        # Corporate large-customer postal codes repeat one PLZ across company names; the first
        # entry's coordinates are as good as any for a radius search.
        postal.setdefault(row[1], (row[1], row[2], ADMIN1.get(row[4], ""), row[9], row[10]))

with open(OUT_POSTAL, "w", encoding="utf-8", newline="") as fh:
    out = csv.writer(fh, delimiter=";", quoting=csv.QUOTE_MINIMAL)
    out.writerow(["plz", "name", "admin1", "lat", "lon"])
    for row in sorted(postal.values()):
        out.writerow(row)

print(f"places: {len(places)}  postal: {len(postal)}  with-german-name: {len(german)}")
