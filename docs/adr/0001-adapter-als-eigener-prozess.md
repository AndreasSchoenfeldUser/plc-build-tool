# 0001 Adapter als eigener Prozess mit JSON-Protokoll

Stand: 25.10.2026, angenommen

## Kontext

TIA Openness V21 braucht .NET Framework 4.8, CODESYS-Skripte laufen in IronPython 2.7 innerhalb von CODESYS.exe, TwinCAT spricht COM. Die Openness-Firewall bindet die Freigabe an den SHA-256 der Exe. Ein hängender Openness-Aufruf darf den Kern nicht mitnehmen.

## Entscheidung

Der Kern (.NET 10) startet je Schritt einen Adapter-Prozess. Er schreibt `request-<schritt>.json`, übergibt den Pfad als Argument und liest `response-<schritt>.json`. Zeitlimit und Abbruch (`Kill` des Prozessbaums) liegen im Kern. Der Exit-Code des Adapters wird nur protokolliert.

## Folgen

- Kern und Fake-Adapter laufen und testen auf jedem Rechner.
- Neue Kern-Versionen ändern die Firewall-Freigabe des TIA-Adapters nicht.
- Jeder Schritt kostet einen Prozessstart. Bei TIA kommt das Öffnen des Projekts dazu; dafür gibt es `run` mit mehreren Schritten in einer Sitzung. Ob das nötig ist, zeigt die Messung in M1.
