# plc-build (Prototyp)

Kommandozeilenwerkzeug, mit dem ein CI-Server SPS-Projekte übersetzt, statisch prüft und ohne Hardware testet. Ein Befehlssatz für alle Engineering-Umgebungen, je Umgebung ein Adapter. Ziel, Architektur und Meilensteine: [CLAUDE.md](CLAUDE.md).

Stand: Meilenstein M0 (Kern und Fake-Adapter).

## Bauen und testen

Voraussetzung: .NET 10 SDK.

```bash
dotnet build PlcBuild.sln
dotnet test PlcBuild.sln
```

Golden Files neu schreiben (nur mit Begründung im Commit): `PLC_BUILD_UPDATE_GOLDEN=1 dotnet test`.

## Ausprobieren

```bash
samples/fake-demo/run-pipeline.sh ok
samples/fake-demo/run-pipeline.sh compile-error
```

oder einzeln, im Verzeichnis `samples/fake-demo`:

```bash
dotnet ../../src/PlcBuild.Cli/bin/Debug/net10.0/plc-build.dll compile --project linie-1/plc
```

## Exit-Codes

| Code | Bedeutung |
|---|---|
| 0 | erfolgreich |
| 1 | Befund im Projekt (Übersetzungsfehler, Regel verletzt, Test rot) |
| 2 | Aufruf oder Konfiguration falsch |
| 3 | Umgebung (Lizenz, Gruppe, Firewall, Adapter fehlt) |
| 4 | Abbruch (Zeitlimit, Adapter abgestürzt, keine gültige Antwort) |

## Aufbau

| Pfad | Inhalt |
|---|---|
| `src/PlcBuild.Cli` | Einstieg `plc-build`, Befehle |
| `src/PlcBuild.Core` | Konfiguration, Adapter-Start, PID-Buchführung, JUnit, SARIF, Manifest |
| `src/PlcBuild.Protocol` | Protokoll-Modell, netstandard2.0, auch für den net48-TIA-Adapter |
| `adapters/fake` | Fake-Adapter mit Testszenarien |
| `tests/PlcBuild.Core.Tests` | xUnit, Golden Files unter `golden/` |
| `samples/fake-demo` | Beispiel-Repository mit Jenkinsfile |
| `docs/adr` | Architekturentscheidungen |
| `docs/messungen.md` | Messprotokoll |
| `.claude/skills` | Openness-Skills von Siemens (MIT, siehe `LICENSE-siemens-tia-portal-ai-extensions`) |
