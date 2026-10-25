# 0003 Festlegungen zum Adapter-Protokoll v1

Stand: 25.10.2026, angenommen

## Kontext

CLAUDE.md Abschnitt 5 gibt Request und Response als Beispiel vor. Für die Umsetzung fehlten einige Details.

## Entscheidung

1. **Antwortdatei**: Der Adapter schreibt `response-<schritt>.json` nach `outDir` aus dem Request. Bei `run` (mehrere Schritte) schreibt er eine Datei je Schritt und hört nach dem ersten Ergebnis ungleich `ok` auf. Fehlende Antworten nach einem solchen Schritt gelten als übersprungen.
2. **Alte Antworten**: Der Kern löscht `response-<schritt>.json` vor dem Start. Eine Antwort aus einem früheren Lauf kann so nicht als aktuelles Ergebnis gelesen werden.
3. **Ersatzantwort**: Liefert der Adapter keine gültige Antwort, schreibt der Kern selbst eine mit `outcome: error`, `generatedBy: "plc-build"` und einem Code: `ADAPTER_TIMEOUT`, `ADAPTER_CRASHED` (Exit-Code ≠ 0 ohne Antwort), `ADAPTER_NO_RESPONSE`, `ADAPTER_INVALID_RESPONSE`, `PROTOCOL_UNSUPPORTED`, `ADAPTER_START_FAILED`. `report` zeigt den Abbruch dadurch als `<error>` in JUnit.
4. **Protokollversion**: Der Kern liest zuerst nur `protocol`. Ist es nicht 1, wird der Rest nicht ausgewertet (Exit 4).
5. **Neue Felder im Request**: `options` für Argumente von der Kommandozeile (bisher nur `archiveFile` bei `archive`), `secrets` aus `adapter.json`.
6. **Neue Felder in der Response**: `environmentIssues[].remedy` für die Handlungsanweisung, `generatedBy` (siehe 3). `location` ist ein Objekt mit optional `file`, `line`, `column`, `network`.
7. **Unbekannte Felder** werden beim Lesen ignoriert. Neue optionale Felder erhöhen `protocol` nicht; geänderte Bedeutung oder Pflichtfelder schon.
8. **Gemeinsames Modell**: Die Klassen liegen in `src/PlcBuild.Protocol` (netstandard2.0, ohne Abhängigkeiten), damit auch der net48-TIA-Adapter sie verwenden kann. Das Projekt steht nicht in der Struktur von CLAUDE.md Abschnitt 3 und ist hiermit ergänzt.

## Folgen

- Ein Adapter kann den Kern nicht mit einer alten oder kaputten Antwort zu Exit 0 oder 1 bringen.
- Der Python-Adapter für CODESYS muss dieselben Feldnamen schreiben; ein JSON-Schema für das Protokoll wäre für M3 hilfreich.
