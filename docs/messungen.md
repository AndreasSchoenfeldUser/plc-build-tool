# Messungen

Je Live-Lauf ein Eintrag: Datum, Agent (CPU, RAM), Engineering-Software mit Update, Projektgröße, Zeit je Schritt, Speicher-Spitze, Ergebnis, Auffälligkeiten. Fehlschläge gehören auch hierher.

## M0, 25.10.2026: Kern mit Fake-Adapter

Keine Engineering-Software, deshalb keine Aussage zu H1 und H2.

- Rechner: MacBook (Apple Silicon, arm64), macOS 26, .NET SDK 10.0.403
- 72 xUnit-Tests in rund 3 s, drei Läufe nacheinander ohne Fehler, danach keine Fake-Adapter-Prozesse übrig
- Prozessstart des Fake-Adapters (apphost) rund 0,1 s je Schritt
- Szenario `hang` mit `--timeout 0.02` (1,2 s): Abbruch nach rund 1,9 s, Exit 4, Prozessbaum beendet
- SARIF-Ausgaben gegen das OASIS-Schema 2.1.0 geprüft: gültig
- Nicht geprüft: Lauf auf einem echten Jenkins, Windows-Agent

## M1: TIA-Grundlauf

(offen)

| Datum | Agent | TIA-Version | Projekt | restore | import | compile | archive | RAM-Spitze | Ergebnis | Auffälligkeiten |
|---|---|---|---|---|---|---|---|---|---|---|
