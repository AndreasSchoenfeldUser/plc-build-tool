# GUI-Prototypen für plc-build: Prompts für Nano Banana 2

`plc-build` hat im Prototyp keine Oberfläche (CLAUDE.md, Abschnitt 1). Die Bilder zeigen, wie die Ergebnisse aussehen könnten, die das Werkzeug ohnehin erzeugt (Exit-Code, Diagnosen, Tests, Manifest, `doctor`-Befunde, Messungen). Gedacht für Produktseite, Gespräche mit Pilotkunden oder als Vorlage für eine spätere Jenkins-Ansicht.

Alle Zahlen, Hashes und Kontonamen sind Beispieldaten.

## Vorgehen in Magnific

1. Modell **Nano Banana 2** wählen, Seitenverhältnis **16:9**, höchste verfügbare Auflösung.
2. Prompt 1 zuerst erzeugen. Das beste Ergebnis bei Prompt 2 und 3 als Referenzbild anhängen und den Satz am Anfang des Prompts stehen lassen („Use the attached image as the design reference …“). So bleiben Farben, Schrift und Seitenleiste über alle drei Bilder gleich.
3. Falsch geschriebene Texte nicht neu würfeln, sondern im selben Verlauf korrigieren: `Change only the text "…" to "…". Keep everything else identical.`
4. Zum Hochskalieren den Upscaler mit niedriger Kreativität bzw. den Precision-Modus nehmen. Mit hoher Kreativität erfindet der Upscaler neue Buchstaben.

Die Prompts sind auf Englisch, weil Layout-Anweisungen so verlässlicher umgesetzt werden. Alles in Anführungszeichen ist Oberflächentext und bleibt Deutsch.

Farblogik in allen drei Bildern: Grün = Exit 0, Rot = Exit 1 (Befund, Entwickler ist dran), Orange = Exit 3/4 (Umgebung oder Abbruch, Agent-Betreuer ist dran). Die Trennung von 1 gegen 3/4 ist das Kernversprechen und soll auf den ersten Blick sichtbar sein.

---

## Prototyp 1: Build-Ergebnis eines Commits

Zielgruppe: SPS-Entwickler. Frage: Was ist kaputt, und wo?

```text
High-fidelity UI design mockup of a desktop web application called "plc-build", rendered as a flat, pixel-sharp screenshot filling a 16:9 frame. No browser chrome, no device, no people, no perspective.

The screen shows the result of one CI build of a PLC project.

Layout: a slim dark slate sidebar on the left (#1E2530) with the lowercase wordmark "plc-build" at the top and four navigation items with simple line icons: "Builds" (active, highlighted), "Agenten", "Projekte", "Messungen". Main area with a light grey background (#F5F6F8) and white cards with a 1px light border and 8px rounded corners.

Header row: large title "linie-1/plc · Build #418". Below it in small grey monospace text: "Commit 3f9c2a1 · main · TIA Portal V21 Update 1". On the far right a large red pill badge with white text: "Exit 1 · Befund im Projekt".

Below the header, a horizontal pipeline of six round step nodes connected by a thin line, each with a label and duration underneath:
"restore 2:41" green check, "import 0:38" green check, "compile 1:21" red cross, "analyze" grey and dimmed, "test" grey and dimmed, "archive" grey and dimmed.

Left main card, wide, titled "Übersetzungsfehler (2)". A clean table with three columns "Schwere", "Objekt", "Meldung":
Row 1: small red tag "Fehler", monospace path "PLC_1/Programmbausteine/Foerderband/FB_Foerderband", message "Operand „#Freigabe“ ist nicht definiert."
Row 2: small red tag "Fehler", monospace path "PLC_1/Programmbausteine/Greifer/FB_Greifer", message "Datentyp von „#Zeit“ passt nicht."
Below the table a collapsed grey row with a chevron: "5 Warnungen".

Right column, narrow card titled "Build-Manifest", label and value pairs, values in monospace:
"Adapter" "tia-v21"
"Basisarchiv" "sha256 9a1e…c40b"
"Quellen" "sha256 77d0…1f2e"
"Gestartet" "25.10.2026 08:01"
"Dauer" "4:40"
Under it three small outlined buttons with download icons: "JUnit-XML", "SARIF", "Manifest".

Visual style: sober, precise industrial engineering software, calm and trustworthy, in the spirit of modern developer tools. Sans-serif typeface like Inter for UI text, monospace like JetBrains Mono for paths and hashes. One accent color, petrol blue (#0E6E7E), for active navigation and links. Status colors: green #2E7D32, red #C62828, grey #9AA0A6. Generous whitespace, strict grid alignment, subtle shadows only.

All text must be crisp, legible and spelled exactly as written above, including German umlauts and the German quotation marks „ “. No lorem ipsum, no extra invented text, no vendor logos, no watermark.
```

---

## Prototyp 2: Agent-Diagnose (`plc-build doctor`)

Zielgruppe: Agent-Betreuer. Frage: Warum baut der Agent nicht, und was muss ich tun?

```text
Use the attached image as the design reference: keep the exact same sidebar, colors, typography, card style and spacing. Only the content of the main area changes.

High-fidelity UI design mockup of the desktop web application "plc-build", rendered as a flat, pixel-sharp screenshot filling a 16:9 frame. No browser chrome, no device, no people.

Left sidebar as before, with "Agenten" now the active navigation item.

Header row: large title "Agent-Diagnose · plc-win-02". Below it in small grey monospace text: "plc-build doctor --deep · 25.10.2026 07:58 · Konto svc-plcbuild". On the far right a large orange pill badge (#E07B00) with white text: "Exit 3 · Umgebung".

Left main card, wide, titled "Prüfungen (8)". A vertical checklist, one row per check, each row with a status icon on the left, a German label, and a short grey detail text on the right:
green check: "TIA Portal V21 installiert" detail "V21 Update 1, PublicAPI vorhanden"
orange cross: "Gruppe „Siemens TIA Openness“" detail "Konto nicht Mitglied"
green check: "Kein LocalSystem-Konto" detail "svc-plcbuild"
green check: "Openness-Firewall" detail "Adapter freigegeben, SHA-256 passt"
green check: "Lizenz STEP 7 Professional" detail "gefunden"
amber warning triangle: "Arbeitsspeicher" detail "24 GB, empfohlen 32 GB"
green check: "S7-PLCSIM Advanced V8.0" detail "installiert"
green check: "Keine weitere TIA-Instanz" detail "0 Prozesse"

The failed row is expanded into a light orange box with an orange left border. Inside, first line in bold monospace: "[NOT_IN_OPENNESS_GROUP]". Below in regular text: "Das Konto „svc-plcbuild“ ist nicht in der lokalen Gruppe „Siemens TIA Openness“. Konto aufnehmen, danach ab- und wieder anmelden oder den Dienst neu starten."

Right column, narrow card titled "Agent", label and value pairs:
"Betriebssystem" "Windows Server 2025"
"CPU" "8 Kerne"
"Arbeitsspeicher" "24 GB"
"Betriebsmodell" "Windows-Dienst"
"Label" "plc-windows"

Below it a second narrow card titled "Nächster Schritt" with a dark code block containing one line of white monospace text: net localgroup "Siemens TIA Openness" svc-plcbuild /add
and a small "Kopieren" button with a copy icon in the top right corner of the code block.

Visual style identical to the reference: sober industrial engineering software, Inter-like sans-serif, JetBrains-Mono-like monospace, petrol blue accent (#0E6E7E). Status colors: green #2E7D32, orange #E07B00 for environment problems, amber #F2A900 for warnings. Note: no red anywhere on this screen, because this is an environment problem, not a finding in the PLC project.

All text must be crisp, legible and spelled exactly as written above, including German umlauts and the German quotation marks „ “. No lorem ipsum, no extra invented text, no vendor logos, no watermark.
```

---

## Prototyp 3: Projekte und Stabilität

Zielgruppe: Teamleitung und Comquent selbst. Frage: Laufen TIA und CODESYS mit denselben Befehlen, und ist der Agent stabil? Das Bild zeigt die Hypothesen H1 bis H3 aus CLAUDE.md.

```text
Use the attached image as the design reference: keep the exact same sidebar, colors, typography, card style and spacing. Only the content of the main area changes.

High-fidelity UI design mockup of the desktop web application "plc-build", rendered as a flat, pixel-sharp screenshot filling a 16:9 frame. No browser chrome, no device, no people.

Left sidebar as before, with "Messungen" now the active navigation item.

Header row: large title "Projekte · Commit 3f9c2a1". Below it in small grey text: "2 Projekte · gleiche Befehle für alle Adapter". On the right a segmented control with "7 Tage" and "30 Tage", "30 Tage" selected.

Top card, full width, titled "Schritte je Projekt". A matrix table. Columns: "Projekt", "Adapter", "restore", "import", "compile", "analyze", "test". Each step cell is a small rounded colored tile with a duration inside.
Row 1: "linie-1/plc", monospace tag "tia-v21", tiles: green "2:41", green "0:38", green "1:18", green "0:52", red "6:12".
Row 2: "verpackung/plc", monospace tag "codesys", tiles: grey "–", grey "–", green "0:44", green "0:21", green "1:05".
Below the table a small legend row with colored dots: green "0 ok", red "1 Befund", grey "2 Konfiguration", orange "3 Umgebung", orange striped "4 Abbruch".

Bottom left card titled "Stabilität · letzte 50 Läufe". A single horizontal strip of 50 small equal squares: 49 green squares and one orange square at position 31. A thin callout points to the orange square with the text "#391 · Zeitlimit · Exit 4". Underneath, two large numbers side by side with small labels: "49 / 50" labeled "ohne Eingriff" and "0" labeled "Restprozesse nach cleanup".

Bottom right card titled "Dauer je Schritt · Median". A clean horizontal bar chart with four bars in petrol blue, labels on the left, values on the right:
"Projekt öffnen" 2:35, "Import" 0:41, "Compile" 1:18, "Test" 6:12.
Thin vertical gridlines, no 3D, no gradients.

Visual style identical to the reference: sober industrial engineering software, Inter-like sans-serif, JetBrains-Mono-like monospace, petrol blue accent (#0E6E7E). Status colors: green #2E7D32, red #C62828, orange #E07B00, grey #9AA0A6. Charts flat and minimal, like a well-designed engineering dashboard.

All text must be crisp, legible and spelled exactly as written above, including German umlauts. No lorem ipsum, no extra invented text, no vendor logos, no watermark.
```
