# 0005 Hash über Quellverzeichnisse im Build-Manifest

Stand: 25.10.2026, angenommen

## Kontext

Das Manifest soll belegen, aus welchen Quellen gebaut wurde: „sortierte Dateiliste, Hash über Pfad und Inhalt“ (CLAUDE.md Abschnitt 6). Die genaue Rechenvorschrift muss festliegen, damit sie sich später nachrechnen lässt.

## Entscheidung

- Datei: SHA-256 des Inhalts, hexadezimal, Kleinbuchstaben.
- Verzeichnis: alle Dateien rekursiv, Pfad relativ zum Verzeichnis mit `/`, ordinal sortiert. Je Datei eine Zeile `<pfad>\0<sha256 des inhalts>\n`. Der Verzeichnis-Hash ist SHA-256 über die UTF-8-Bytes aller Zeilen. Zusätzlich steht die Anzahl der Dateien im Manifest.
- Leere Verzeichnisse zählen nicht.

## Folgen

- Gleicher Hash auf Windows und Linux nur bei gleichem Dateiinhalt. Mit `core.autocrlf=true` auf dem Windows-Agenten ändern sich Zeilenenden und damit der Hash. Für Quellverzeichnisse im Kundenrepository `.gitattributes` mit festen Zeilenenden empfehlen.
- Nachrechnen ist mit wenigen Zeilen Skript möglich (siehe `HashingTests`).
