# -*- coding: utf-8 -*-
"""Mechanische Vollpruefung aller Sprachdateien.

Sucht die Faelle, die man beim Lesen uebersieht:
  1. Schluessel, die der Code anfordert, die es aber nicht gibt.
     LocalizedStrings.GetString gibt dann den SCHLUESSELNAMEN zurueck -
     in der Oberflaeche steht dann woertlich "Menu_Rate".
  2. Schluessel in den Dateien, die niemand anfordert (tote Eintraege).
  3. Leere oder nur aus Leerzeichen bestehende Werte.
  4. Werte, die genauso heissen wie ihr Schluessel (nicht uebersetzt).
  5. Platzhalter {0}, die zwischen den Sprachen abweichen -> Ausnahme zur Laufzeit.
  6. Abgeschnittenes: Werte, die auf ... oder … enden, ohne dass es Absicht ist.
  7. Zeichensalat: einsame Ersetzungszeichen, doppelte Leerzeichen, Rand-Leerzeichen.
"""
import io, os, re, sys, glob
import xml.etree.ElementTree as ET

APP  = r"D:\Spieglein\AirPlayReceiver\src\AirPlayReceiver.App"
STR  = os.path.join(APP, "Strings")
NL   = chr(10)
ELL  = chr(0x2026)

sprachen = sorted(os.path.basename(os.path.dirname(p))
                  for p in glob.glob(os.path.join(STR, "*", "Resources.resw")))

werte = {}
for l in sprachen:
    baum = ET.parse(os.path.join(STR, l, "Resources.resw"))
    werte[l] = {d.get("name"): (d.find("value").text or "") for d in baum.getroot().findall("data")}

# --- Schluessel, die der Code anfordert -----------------------------------
angefordert = set()
for wurzel, dirs, dateien in os.walk(APP):
    if "obj" in wurzel or "bin" in wurzel:
        continue
    for f in dateien:
        if not f.endswith((".cs", ".xaml")):
            continue
        t = io.open(os.path.join(wurzel, f), encoding="utf-8", errors="replace").read()
        angefordert |= set(re.findall(r'GetString\("([^"]+)"\)', t))

# StringKey aus den Tabellen (ColorSchemes / TextSizes) kommen indirekt
for f in ("Services/ColorSchemes.cs", "Services/TextSizes.cs"):
    t = io.open(os.path.join(APP, f.replace("/", os.sep)), encoding="utf-8").read()
    angefordert |= set(re.findall(r'"(Color_\w+|Size_\w+)"', t))

meldungen = []
def sag(schwere, text):
    meldungen.append("%-8s %s" % (schwere, text))

# 1 + 2
vorhanden = set(werte[sprachen[0]])
fehlt = sorted(angefordert - vorhanden)
tot   = sorted(vorhanden - angefordert)
if fehlt: sag("FEHLER", "Vom Code angefordert, aber in KEINER Datei: %s" % fehlt)
if tot:   sag("hinweis", "In den Dateien, aber nirgends angefordert: %s" % tot)

# Je Sprache
for l in sprachen:
    d = werte[l]
    fehlend = sorted(angefordert - set(d))
    if fehlend:
        sag("FEHLER", "%s: fehlt -> Oberflaeche zeigt den Schluesselnamen: %s" % (l, fehlend))
    for k, v in sorted(d.items()):
        if not v.strip():
            sag("FEHLER", "%s / %s: leer" % (l, k))
        if v.strip() == k:
            sag("FEHLER", "%s / %s: Wert = Schluesselname, nicht uebersetzt" % (l, k))
        if v != v.strip():
            sag("WARNUNG", "%s / %s: Leerzeichen am Rand [%s]" % (l, k, v))
        # Zeilenweise pruefen: sonst wird jede Leerzeile zu einem
        # scheinbaren Doppel-Leerzeichen und die Ausgabe ist nur Rauschen.
        if any("  " in zeile for zeile in v.split(NL)):
            sag("hinweis", "%s / %s: doppeltes Leerzeichen" % (l, k))
        if chr(0xFFFD) in v:
            sag("FEHLER", "%s / %s: Ersetzungszeichen U+FFFD" % (l, k))
        if v.rstrip().endswith("..."):
            sag("WARNUNG", "%s / %s: endet auf drei Punkte statt Auslassungszeichen: %s" % (l, k, v[-20:]))

# 5 Platzhalter
ph = lambda t: sorted(re.findall(r"\{\d+\}", t))
ref = werte["en-US"] if "en-US" in werte else werte[sprachen[0]]
for l in sprachen:
    for k in sorted(set(werte[l]) & set(ref)):
        if ph(werte[l][k]) != ph(ref[k]):
            sag("FEHLER", "%s / %s: Platzhalter %s statt %s" % (l, k, ph(werte[l][k]), ph(ref[k])))

# 6 Auslassungszeichen: wo eines steht, sollte es ueberall stehen
for k in sorted(vorhanden):
    mit = [l for l in sprachen if werte[l].get(k, "").rstrip().endswith(ELL)]
    if mit and len(mit) != len(sprachen):
        ohne = [l for l in sprachen if l not in mit]
        sag("WARNUNG", "%s: Auslassungszeichen nur in %s, nicht in %s" % (k, mit, ohne))

# Uebersicht
kopf = []
kopf.append("Sprachen: %s" % ", ".join(sprachen))
kopf.append("Schluessel je Datei: %s" % sorted({len(v) for v in werte.values()}))
kopf.append("Vom Code angefordert: %d" % len(angefordert))
kopf.append("")

if not meldungen:
    kopf.append("Keine Beanstandung.")
else:
    fehler = [m for m in meldungen if m.startswith("FEHLER")]
    kopf.append("%d Meldungen, davon %d FEHLER" % (len(meldungen), len(fehler)))
    kopf.append("")

sys.stdout.buffer.write(NL.join(kopf + meldungen).encode("utf-8"))
