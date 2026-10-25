# CLAUDE.md: Prototyp PLC Build Tool (`plc-build`)

> Diese Datei als `CLAUDE.md` in das Wurzelverzeichnis eines neuen, leeren Repositorys kopieren. Sie beschreibt Ziel, Architektur, Schnittstellen, Herstellerfakten und Arbeitsweise für den Prototyp. Stand der Herstellerfakten: 25.10.2026. Die Recherche dazu steht im Website-Repository unter `Produkte/plc-build-tool/Machbarkeit-und-Notwendigkeit-2026-10-25.md`.

## 0. Start

Erste Sitzung in Claude Code:

1. Diese Datei lesen, dann Meilenstein M0 (Abschnitt 9) planen und mit Tests beginnen.
2. Für die TIA-Arbeit die offiziellen Openness-Skills von Siemens einbinden (MIT-Lizenz, 32 Skills, Stand V21):
   ```bash
   git clone --depth 1 https://github.com/siemens/tia-portal-ai-extensions /tmp/tia-ai-ext
   mkdir -p .claude/skills
   cp -R /tmp/tia-ai-ext/openness_development/skills/* .claude/skills/
   cp /tmp/tia-ai-ext/LICENSE .claude/skills/LICENSE-siemens-tia-portal-ai-extensions
   ```
   Wichtig für dieses Projekt: `openness-base`, `session-and-project`, `licensing-and-firewall`, `openness-testing`, `blocks`, `plc-data-types`, `engineering-objects`, `performance-and-caching`, `crash-diagnosis`.
3. Ab M1 läuft Claude Code direkt auf der Windows-VM mit TIA Portal, damit Builds und Live-Tests dort ausgeführt werden können. M0 geht auf jedem Rechner (macOS, Linux, Windows).

## 1. Worum es geht

`plc-build` ist ein Kommandozeilenwerkzeug, das Jenkins, GitLab CI oder Azure DevOps auf einem Windows-Build-Agenten aufrufen, um SPS-Projekte zu übersetzen, statisch zu prüfen und ohne Hardware zu testen. Ein Befehlssatz für alle Engineering-Umgebungen, je Umgebung ein Adapter. Ergebnis: Exit-Code, JUnit-XML, SARIF und ein Build-Manifest.

Produkt von Comquent GmbH (Industrial DevOps, Puchheim). Die Produktseite beschreibt es als „das Stück zwischen CI-Server und Engineering-Umgebung, das bisher jedes Team selbst schreibt“.

### Was der Prototyp beweisen soll

| # | Hypothese | Gemessen an |
|---|---|---|
| H1 | TIA Portal V21 läuft über Openness ohne Oberfläche stabil auf einem CI-Agenten | 50 aufeinanderfolgende Builds ohne manuellen Eingriff, keine Restprozesse |
| H2 | Die Laufzeit einer Pipeline ist für Commit-Builds tragbar | Zeiten je Schritt (Projekt öffnen, Import, Compile, Test) im Messprotokoll |
| H3 | Ein Ergebnismodell passt für TIA und CODESYS ohne Sonderfälle im Kern | Kern-Code enthält keine herstellerspezifischen Verzweigungen |
| H4 | Umgebungsfehler lassen sich vor dem Build erkennen und klar melden | `doctor` erkennt jede Fehlkonfiguration aus Abschnitt 7.1 |

Ein Ergebnis „geht nicht“ ist auch ein Ergebnis. Messwerte und Fehlschläge kommen in `docs/messungen.md`.

### Was nicht dazugehört

- Kein CI-Server, keine Oberfläche, keine Orchestrierung.
- Kein Laden auf reale Steuerungen. Deployment bleibt ein freigegebener Schritt außerhalb des Werkzeugs.
- Keine Lizenzen, keine Herstellersoftware, keine Hersteller-DLLs im Repository.
- Keine Testfälle schreiben. Tests entstehen im Testwerkzeug der Umgebung.
- Im Prototyp nicht: TIA V19/V20, Safety-Programme (F-CPU), Studio 5000, Container.

## 2. Architektur

```
Jenkins / GitLab / Azure
        │  bat "plc-build compile --project linie-1/plc"
        ▼
┌──────────────────────────────┐
│ plc-build (Kern, .NET 10)    │  liest plc.yml, startet Adapter als Prozess,
│                              │  Zeitlimit, Ergebnis → JUnit/SARIF/Manifest,
│                              │  Exit-Code, PID-Buchführung für cleanup
└──────────────┬───────────────┘
               │ request.json  →   ← response.json   (Protokoll, Abschnitt 5)
   ┌───────────┼──────────────┬───────────────┐
   ▼           ▼              ▼               ▼
 fake       tia-v21        codesys         twincat (später)
 .NET 10    C#, net48      IronPython 2.7  C#, COM
            Openness V21   in CODESYS.exe  Automation Interface
```

**Warum Adapter als eigene Prozesse:**

- TIA Openness V21 verlangt .NET Framework 4.8, CODESYS-Skripte laufen in IronPython 2.7 innerhalb von CODESYS, TwinCAT spricht COM. Ein gemeinsamer Prozess ginge nicht.
- V21 und V19/V20 brauchen getrennte Binaries. Jede TIA-Version bekommt ihren eigenen Adapter.
- Die Openness-Firewall prüft den SHA-256 der Exe. Ändert sich nur der Kern, bleibt die Freigabe des Adapters gültig.
- Ein hängender Adapter wird vom Kern nach Zeitlimit beendet, ohne dass der Kern mitstirbt.
- Siemens misst Openness-Aufrufe im NUnit-Prozess 3- bis 5-mal langsamer als in einer eigenen Konsolen-Anwendung (Skill `openness-testing`). Eigene Prozesse umgehen das auch in Tests.
- Der Kern ist plattformunabhängig und wird mit dem Fake-Adapter auf jedem Rechner getestet.

## 3. Repository-Struktur

```
plc-build/
├── CLAUDE.md
├── PlcBuild.sln                  Kern, Fake-Adapter, Tests (baut überall)
├── src/
│   ├── PlcBuild.Cli/             Einstieg, Befehle, Exit-Codes
│   └── PlcBuild.Core/            Konfiguration, Protokoll, Adapter-Start, Ergebnis-Modell,
│                                 JUnit-, SARIF- und Manifest-Ausgabe, PID-Buchführung
├── adapters/
│   ├── fake/                     .NET 10, Szenarien für Tests (ok, Fehler, Lizenz fehlt, hängt)
│   ├── tia-v21/                  eigene Solution, C#, net48, baut nur auf Windows mit TIA V21
│   └── codesys/                  adapter.py (IronPython-2.7-kompatibel) + Test-Manager-Aufruf
├── tests/
│   ├── PlcBuild.Core.Tests/      xUnit, Golden Files für JUnit/SARIF/Manifest
│   └── live/                     nur mit PLC_BUILD_LIVE=1 auf dem Windows-Agenten
├── samples/
│   ├── tia-demo/                 Beispiel-Repository: plc.yml, base/*.zap21 (LFS), src/plc, rules, Jenkinsfile
│   └── codesys-demo/
├── tools/
│   └── Register-OpennessFirewall.ps1   Freigabe des Adapters für Administratoren
└── docs/
    ├── adr/                      Architekturentscheidungen, eine Datei je Entscheidung
    └── messungen.md              Zeiten, Speicher, Fehlschläge je Lauf
```

## 4. Befehle und Exit-Codes

```
plc-build doctor   [--project <id>]          Umgebung prüfen, ohne Projekt zu öffnen
plc-build restore  --project <id>            Basisprojekt aus dem Archiv in ein Arbeitsverzeichnis
plc-build import   --project <id>            Textquellen aus Git ins Projekt
plc-build compile  --project <id>            Übersetzen
plc-build analyze  --project <id>            Statische Prüfung mit Regelwerk aus dem Repository
plc-build test     --project <id>            Tests auf Simulation oder Usermode-Laufzeit
plc-build report   --project <id> --junit <dir> [--sarif <dir>]
plc-build archive  --project <id> --out <file>
plc-build cleanup  [--project <id>]          nur selbst gestartete Prozesse beenden
plc-build run      --project <id> --steps restore,import,compile,analyze,test
```

`run` führt mehrere Schritte in einer Adapter-Sitzung aus. Bei TIA heißt das: Projekt einmal öffnen statt je Schritt. Ob das nötig ist, entscheidet die Messung in M1 (H2). Die Einzelbefehle bleiben, weil sie im Jenkinsfile lesbarer sind.

Gemeinsame Optionen: `--config <pfad>` (Vorgabe `plc.yml` im aktuellen Verzeichnis), `--out <dir>` (Vorgabe `out/`), `--timeout <min>`, `--verbose`.

| Exit-Code | Bedeutung | Beispiel | Wirkung in Jenkins |
|---|---|---|---|
| 0 | Schritt erfolgreich | Compile ohne Fehler | grün |
| 1 | Befund im Projekt | Übersetzungsfehler, Regel verletzt, Test fehlgeschlagen | rot, Entwickler ist dran |
| 2 | Aufruf oder Konfiguration falsch | Projekt-ID fehlt in `plc.yml`, Datei nicht gefunden | rot, Repository ist dran |
| 3 | Umgebung | Lizenz fehlt, Konto nicht in Gruppe, Firewall, TIA-Version passt nicht | rot, Agent-Betreuer ist dran |
| 4 | Abbruch | Zeitlimit, Adapter abgestürzt, keine Antwort | rot, Agent-Betreuer ist dran |

Die Trennung von 1 gegen 3/4 ist ein Kernversprechen. Im Zweifel gilt: Nur wenn der Adapter eine Antwort mit `outcome: failed` liefert, ist es ein Befund (1).

Warnungen ändern den Exit-Code nicht. `analyze` kennt im Regelwerk die Stufe „Warnung“ für Regeln, deren Altbestand noch nicht bereinigt ist.

## 5. Adapter-Protokoll

Der Kern schreibt `request.json`, startet den Adapter mit dem Pfad als Argument und erwartet `response.json`. Fehlt die Antwort, ist sie ungültig oder läuft das Zeitlimit ab, ergibt das Exit-Code 4. Der Exit-Code des Adapter-Prozesses wird nur protokolliert, weil ihn nicht jede Umgebung zuverlässig setzt (CODESYS `--runscript` dokumentiert keinen).

Arbeitsverzeichnis je Projekt: `out/.plc-build/<projekt-id mit / → _>/` mit `request-<schritt>.json`, `response-<schritt>.json`, `pids.json`, `adapter-<schritt>.log`.

**request.json**

```json
{
  "protocol": 1,
  "step": "compile",
  "steps": ["compile"],
  "project": {
    "id": "linie-1/plc",
    "adapter": "tia-v21",
    "root": "C:\\ws\\repo\\linie-1\\plc",
    "workDir": "C:\\ws\\repo\\out\\.plc-build\\linie-1_plc\\work",
    "settings": { "baseArchive": "base/Linie1.zap21", "sources": "src/plc", "plc": "PLC_1" }
  },
  "outDir": "C:\\ws\\repo\\out\\.plc-build\\linie-1_plc",
  "timeoutSeconds": 1800,
  "secrets": { "umacUserEnv": "PLC_BUILD_UMAC_USER", "umacPasswordEnv": "PLC_BUILD_UMAC_PASSWORD" }
}
```

Geheimnisse stehen nie im Request, nur die Namen der Umgebungsvariablen.

**response.json**

```json
{
  "protocol": 1,
  "step": "compile",
  "outcome": "failed",
  "summary": "2 Fehler, 5 Warnungen",
  "durationMs": 81234,
  "tool": { "name": "TIA Portal", "version": "V21 Update 1", "components": { "Openness": "21.0", "Test Suite": "V21" } },
  "diagnostics": [
    {
      "severity": "error",
      "source": "compile",
      "object": "PLC_1/Programmbausteine/Foerderband/FB_Foerderband",
      "location": null,
      "code": null,
      "message": "Operand „#Freigabe“ ist nicht definiert."
    }
  ],
  "tests": [
    { "suite": "FB_Greifer", "name": "Test_Schliessen_Timeout", "outcome": "failed", "durationMs": 1200,
      "message": "Erwartet TRUE, ist FALSE", "expected": "TRUE", "actual": "FALSE" }
  ],
  "artifacts": [ { "kind": "archive", "path": "out/Linie1_418.zap21", "sha256": "…" } ],
  "processes": [ { "pid": 4711, "name": "Siemens.Automation.Portal", "startTime": "2026-10-25T08:01:12Z" } ],
  "environmentIssues": []
}
```

- `outcome`: `ok` · `failed` (Befund, Exit 1) · `environment` (Exit 3, Gründe in `environmentIssues` mit `code` und Handlungsanweisung) · `error` (unerwarteter Fehler im Adapter, Exit 4).
- `location` nur füllen, wenn die Umgebung es liefert. Nichts aus Meldungstexten erfinden; eine Netzwerknummer bei TIA nur, wenn der Pilot zeigt, dass sie verlässlich im Meldungstext steht.
- `processes`: jeden Prozess, den der Adapter gestartet hat, mit PID und Startzeit. Der Kern führt sie in `pids.json` zusammen.
- Protokolländerungen erhöhen `protocol`. Der Kern lehnt unbekannte Versionen mit Exit 4 ab.

## 6. Ausgaben

`report` liest alle `response-*.json` eines Projekts und schreibt:

- **JUnit-XML** (`out/junit/<projekt>.xml`) im Format, das das Jenkins-JUnit-Plugin, GitLab und Azure DevOps lesen: `testsuites` → `testsuite` je Schritt → `testcase`. Compile: ein Testfall je Objekt mit Fehlern, sonst ein grüner Testfall „compile <projekt>“. Analyze: ein Testfall je verletzter Regel und Objekt. Test: die Testfälle der Umgebung, bei Fehlschlag mit erwartetem und tatsächlichem Wert in `failure`.
- **SARIF 2.1.0** (`out/sarif/<projekt>.sarif`) für Compile- und Analyse-Befunde. CODESYS und TwinCAT exportieren selbst SARIF; dann übernehmen und nur um `plc-build`-Metadaten ergänzen.
- **Build-Manifest** (`out/manifest/<projekt>.json`): Commit (aus `GIT_COMMIT` oder `git rev-parse`), Projekt, Adapter, Werkzeugversionen, SHA-256 von Basisarchiv und Quellen (sortierte Dateiliste, Hash über Pfad und Inhalt), erzeugte Artefakte mit SHA-256, Ergebnis je Schritt, Zeitstempel. Es belegt, womit ein Stand gebaut und geprüft wurde. Nicht als „CRA-konform“ bezeichnen; es ist ein Baustein für Testberichte und Änderungsnachweise.

Ausgaben deterministisch halten (sortierte Listen, feste Feldreihenfolge), damit Golden-File-Tests stabil sind. Zeitstempel und Dauer in Tests über eine injizierte Uhr.

## 7. Adapter TIA Portal V21

Stand der Fakten 25.10.2026; vor dem Einbau einer Funktion die Seite im Openness-V21-Handbuch und den passenden Siemens-Skill lesen. Basis-URL Handbuch: `https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/`

### 7.1 Build-Agent und `doctor`

`doctor` prüft, ohne ein Projekt zu öffnen, und meldet je Befund einen Code und den Schritt zur Behebung:

| Code | Prüfung | Hintergrund |
|---|---|---|
| `TIA_NOT_INSTALLED` | TIA Portal V21 installiert, `PublicAPI\V21\net48` vorhanden | Pfad über Registry `HKLM\SOFTWARE\Siemens\Automation\Openness` ermitteln, nicht fest verdrahten |
| `NOT_IN_OPENNESS_GROUP` | aktuelles Konto in lokaler Gruppe „Siemens TIA Openness“ | sonst `EngineeringSecurityException`; nach Aufnahme ab- und anmelden bzw. Dienst neu starten |
| `LOCAL_SYSTEM` | Prozess läuft nicht als LocalSystem | LocalSystem wirft immer `EngineeringSecurityException`; eigenes Konto verwenden |
| `FIREWALL_NOT_ALLOWED` | Adapter-Exe dauerhaft in der Openness-Firewall freigegeben, Hash passt | Freigabe hängt an Pfad, Änderungsdatum und SHA-256; Registry-Format aus der Handbuchseite „TIA Portal Openness firewall“ übernehmen, nicht raten |
| `LICENSE_MISSING` | STEP 7 Professional, für `analyze`/`test` Test Suite Advanced | sicher nur durch Probe: TIA ohne UI starten, `LicenseNotFoundException` abfangen; deshalb `doctor --deep` |
| `PLCSIM_MISSING` | S7-PLCSIM Advanced V8.0 installiert | gehört zu V21 |
| `LOW_MEMORY` | ≥ 16 GB RAM, Warnung unter 32 GB bei großen Projekten | Systemvoraussetzungen V21 |
| `RUNNING_TIA_SAME_USER` | keine fremde TIA-Instanz desselben Kontos aktiv | Openness erreicht nur Prozesse desselben Benutzers; parallele Builds je Konto vermeiden |

`tools/Register-OpennessFirewall.ps1` schreibt die dauerhafte Freigabe für eine gegebene Exe (Administrator). Nach jedem neuen Build des Adapters erneut ausführen; das gehört später in ein Installationspaket.

Betriebsmodell des Agenten, beides in M1 testen und in `docs/messungen.md` festhalten:
- Windows-Dienst unter eigenem Konto (Siemens beschreibt das offiziell, nur neue Instanz ohne UI).
- Jenkins-Inbound-Agent als geplante Aufgabe bei Anmeldung eines Autologon-Kontos, Bildschirmsperre aus (Empfehlung aus dem Comquent-Whitepaper, weil Werkzeuge wie versiondog und Copia eine angemeldete Sitzung fordern).

### 7.2 Openness-Grundlagen

- C#, .NET Framework 4.8, Referenzen aus `PublicAPI\V21\net48` (modular: `Siemens.Engineering.Base.dll` immer, Fachbereiche in eigenen DLLs). Die DLLs nicht ins Repository kopieren.
- Assembly-Auflösung über das NuGet-Paket `Siemens.Collaboration.Net.TiaPortal.Openness.Resolver` in Version 2 (nur V21 und neuer). Resolver in einer Methode registrieren, die keine Siemens-Typen enthält; Openness-Code in eine eigene Methode mit `[MethodImpl(MethodImplOptions.NoInlining)]`.
- Start: `new TiaPortal(TiaPortalMode.WithoutUserInterface)`. Eine TIA-Instanz je Projekt. `Dispose()` am Ende; danach können `FileStorage.Server.exe`-Prozesse übrig bleiben, deshalb PID-Buchführung.
- Nie `Dispose()` auf Handles aus `TiaPortal.GetProcesses()` aufrufen: das beendet die laufende Instanz.
- UMAC-geschützte Archive brauchen beim `Retrieve` einen `UmacDelegate`; Anmeldedaten nur aus Umgebungsvariablen, nie protokollieren.

### 7.3 Schritte

Die Methodennamen in der Tabelle sind Wegweiser. Exakte Signaturen (Parametertypen wie `FileInfo`/`DirectoryInfo`, Enum-Werte) aus dem V21-Handbuch und den Siemens-Code-Snippets übernehmen.

| Schritt | Umsetzung | Prüfen im Pilot |
|---|---|---|
| restore | `Projects.Retrieve(<.zap21>, <workDir>)`; Basisprojekt enthält Hardware, Netzwerk, Variablentabellen, globale Bibliotheken | Dauer, UMAC |
| import | SIMATIC SD (`.s7dcl`, `.s7res`) je Ordner mit `PlcBlockComposition.ImportFromDocuments(DirectoryInfo, name, ImportDocumentOptions)`, Ergebnis `DocumentImportResult` auswerten; PLC-Datentypen über die eigene Import-Seite; Variablentabellen nur, wenn als SimaticML-XML im Repository | importierte OBs behalten ihren Typ? (laut Handbuch werden sie Zyklus-OBs); gleichnamige Objekte aus verschiedenen Namensräumen |
| compile | `plcSoftware.GetService<ICompilable>().Compile()`; `CompilerResult.Messages` rekursiv durchlaufen, `Path` als `object`, `Description` als `message`; `ErrorCount > 0` → `failed` | steht die Netzwerknummer verlässlich in `Description`? |
| analyze | `project.GetService<TestSuiteService>()`, Style Guide über `RuleSetExecutor.Run(...)`, Regelwerk per `LoadFromFile` aus `rules/` | Ergebnisobjekte → `diagnostics` |
| test | Application Test im Modus SystemManaged (legt PLCSIM-Advanced-Instanz selbst an, braucht keine PLCSIM-Lizenz, aber Test Suite Advanced); Testfälle aus Dateien laden | nur S7-1500-Familie inkl. ET 200SP/ET 200pro; nicht 1514SP, 1518T, Software Controller im SystemManaged-Modus; keine S7-1200 |
| archive | `project.Archive(<datei>, ProjectArchivationMode.Compressed)` nur auf dem Hauptzweig (entscheidet die Pipeline) | Prüfsumme ins Manifest |
| cleanup | nur PIDs aus `pids.json` beenden, und nur wenn Startzeit übereinstimmt | auch nach abgebrochenem Build |

Für `analyze` und `test` die Namespaces `Siemens.Engineering.TestSuite.*` gegen das V21-Handbuch prüfen; die Belege stammen aus dem Function Manual für V19. Ist ein Teil in V21 anders, ADR schreiben.

Grenzen im Prototyp: keine F-CPU (Compile nur als Software mit Anmeldung, VCI-Vergleich braucht Safety-Anmeldung), keine know-how-geschützten Bausteine, kein Laden in reale Steuerungen.

### 7.4 Beispielprojekt

`samples/tia-demo/`: eine S7-1500, ohne Safety, zwei FBs in SCL und KOP, ein UDT, ein DB, eine Variablentabelle im Basisprojekt, zwei Style-Guide-Regeln, zwei Application-Testfälle (einer absichtlich rot schaltbar über eine Konstante). Kein Kundenprojekt verwenden.

## 8. Adapter CODESYS (ab M3)

- Aufruf durch den Kern: `CODESYS.exe --profile="<exakter Profilname>" --noUI --runscript="adapters\codesys\adapter.py" --scriptargs="<request.json>"`. `--noUI` wirkt nur mit `--runscript`. Ohne `--textPrompts` bestätigt CODESYS Rückfragen mit dem Vorgabewert.
- `adapter.py` muss unter IronPython 2.7 laufen: keine f-Strings, kein `pathlib`, `io.open` mit Encoding, `json` aus der Standardbibliothek.
- Projekt öffnen, `application.build()` bzw. `generate_code()` (nur primäres Projekt, Signatur in der ScriptEngine-Doku prüfen), Meldungen über `system.get_message_objects()`; Kategorien werden per GUID identifiziert und können je OEM abweichen.
- Analyze: `system.commands["staticanalysis","runandexportosarif"].execute("--sariffile=…","--rulesfile=….csa")` (genaue Syntax aus der Handbuchseite übernehmen). SARIF direkt durchreichen.
- Test: Test Manager über eigene Kommandozeile `--executecommand="TestManager ExecuteScript"` mit `--save-xml`, `--exitafterexecutecommand`; Exit-Codes 0 bis 19 laut Handbuch (19 = keine Lizenz → `environment`). XML-Bericht in `tests[]` übersetzen. Im Forum gemeldet: in älteren Versionen kein `--noUI`, Exit-Code immer 0. Deshalb Ergebnis immer aus dem XML lesen.
- Laufzeit für Tests: CODESYS Control Win im Demo-Modus ist zeitlich begrenzt (oft genannt: 2 Stunden). Für Dauerbetrieb Lizenz einplanen.
- Lizenz: Professional Developer Edition je Agent (Git, Static Analysis, Test Manager, File-Based Storage).
- OEM-Varianten (WAGO e!COCKPIT, Schneider Machine Expert) nicht im Prototyp; Name der Exe und Optionen weichen ab.

## 9. Meilensteine

**M0: Kern und Fake-Adapter (jeder Rechner)**
- CLI mit allen Befehlen, `plc.yml` laden und validieren, Protokoll v1, Adapter-Start mit Zeitlimit und Kill, PID-Buchführung, `report` mit JUnit, SARIF, Manifest, Exit-Codes 0 bis 4.
- Fake-Adapter mit Szenarien `ok`, `compile-error`, `test-failure`, `license-missing`, `hang`, `crash`, `no-response`.
- Abnahme: alle Szenarien als Tests mit erwartetem Exit-Code und Golden Files; `samples/fake-demo/Jenkinsfile` läuft auf einem Jenkins mit beliebigem Agenten.

**M1: TIA-Grundlauf (Windows-VM mit TIA V21)**
- `doctor`, `restore`, `import`, `compile`, `archive`, `cleanup`, `report` für `samples/tia-demo`.
- Abnahme: Commit mit Fehler im FB gibt Exit 1 und zeigt Objekt und Meldung in Jenkins; nach Korrektur grün; Agent ohne Gruppenmitgliedschaft gibt Exit 3 mit `NOT_IN_OPENNESS_GROUP`; 50 Läufe in Folge ohne Restprozess (H1); Zeiten je Schritt in `docs/messungen.md` (H2); Betrieb als Dienst und als Inbound-Agent verglichen.

**M2: TIA-Prüfung und Test**
- `analyze` (Style Guide), `test` (Application Test SystemManaged), optional `run` mit einer Sitzung, falls M1 zeigt, dass das Öffnen dominiert.
- Abnahme: verletzte Regel erscheint in SARIF und JUnit; roter Testfall zeigt erwarteten und tatsächlichen Wert; Jenkins-`lock` verhindert parallele Simulation.

**M3: CODESYS-Adapter**
- `compile`, `analyze` (SARIF), `test` (Test Manager → JUnit) für `samples/codesys-demo`.
- Abnahme: H3, also kein CODESYS-Sonderfall im Kern; eine Jenkins-Matrix baut TIA- und CODESYS-Demo mit denselben Befehlen.

**Danach, nicht im Prototyp:** TwinCAT (Automation Interface, `ExportToSarif()`, Tests über eigenen TcUnit-Runner oder Beckhoff TF1140), Studio 5000 (Logix Designer SDK ab V37, aufbauend auf Rockwells `l5xgit` und `ra-logix-cicd`), TIA V19/V20, Installationspaket mit Firewall-Freigabe.

## 10. Konfiguration `plc.yml`

Liegt im Kundenrepository. Dieselbe Datei soll später die PLC Git Bridge lesen; deshalb nur Felder aufnehmen, die beide Werkzeuge verstehen, und unbekannte Felder beim Laden tolerieren.

```yaml
version: 1
projects:
  - id: linie-1/plc
    adapter: tia-v21
    path: linie-1/plc
    tia:
      baseArchive: base/Linie1.zap21      # Git LFS
      sources: src/plc                    # SIMATIC SD und SimaticML-XML
      plc: PLC_1                          # Gerätename im Basisprojekt
      styleGuide: rules/styleguide.xml
      testCases: tests/
      umac: false
  - id: verpackung/plc
    adapter: codesys
    path: verpackung/plc
    codesys:
      exe: "C:\\Program Files\\CODESYS 3.5.22.1\\CODESYS\\Common\\CODESYS.exe"
      profile: "CODESYS V3.5 SP22 Patch 1"
      project: Verpackung.project
      application: Application
      staticAnalysisRules: rules/verpackung.csa
      testScript: tests/Verpackung.testscript
```

Validierung mit klaren Meldungen und Exit 2. Pfade relativ zu `path`.

## 11. Jenkinsfile-Muster

```groovy
pipeline {
  agent { label 'plc-windows' }
  options { timeout(time: 90, unit: 'MINUTES'); disableConcurrentBuilds() }
  environment { PROJEKT = 'linie-1/plc' }
  stages {
    stage('Umgebung') { steps { bat 'plc-build doctor --project %PROJEKT%' } }
    stage('Build') {
      steps {
        bat 'plc-build restore --project %PROJEKT%'
        bat 'plc-build import  --project %PROJEKT%'
        bat 'plc-build compile --project %PROJEKT%'
        bat 'plc-build analyze --project %PROJEKT%'
      }
    }
    stage('Test') {
      steps { lock('plcsim') { bat 'plc-build test --project %PROJEKT%' } }
    }
    stage('Archiv') {
      when { branch 'main' }
      steps { bat 'plc-build archive --project %PROJEKT% --out out\\Linie1_%BUILD_NUMBER%.zap21' }
    }
  }
  post {
    always {
      bat 'plc-build report --project %PROJEKT% --junit out\\junit --sarif out\\sarif'
      junit allowEmptyResults: true, testResults: 'out/junit/*.xml'
      archiveArtifacts artifacts: 'out/manifest/*.json, out/sarif/*.sarif', allowEmptyArchive: true
      bat 'plc-build cleanup || exit 0'
    }
  }
}
```

Später: Exit-Code 3/4 per `returnStatus` abfangen und den Build als Infrastrukturfehler kennzeichnen (z. B. eigene Beschreibung, Benachrichtigung an den Agent-Betreuer).

## 12. Arbeitsweise in diesem Repository

- **Tests zuerst** für den Kern (xUnit). Golden Files unter `tests/PlcBuild.Core.Tests/golden/`; Änderung an einem Golden File nur mit Begründung im Commit.
- **Live-Tests** gegen echte Engineering-Software nur mit `PLC_BUILD_LIVE=1`, nur auf dem Windows-Agenten, immer über den Adapter-Prozess. Jeder Live-Lauf prüft, dass mehr als 0 Tests ausgeführt wurden, und dass danach kein von ihm gestarteter Prozess übrig ist.
- **Herstellerfakten**: Vor dem Einbau einer Schnittstelle die Handbuchseite oder den Siemens-Skill lesen. Was nicht belegt ist, als Annahme im Code-Kommentar oder ADR kennzeichnen. Keine API-Namen raten; nicht kompilierender Openness-Code ist schlimmer als ein offener TODO.
- **Entscheidungen** als kurze ADR in `docs/adr/NNNN-titel.md` (Kontext, Entscheidung, Folgen).
- **Messungen** nach jedem Live-Lauf in `docs/messungen.md`: Datum, Agent (CPU, RAM), TIA-Version mit Update, Projektgröße, Zeit je Schritt, Speicher-Spitze, Ergebnis, Auffälligkeiten.
- **Sicherheit**: keine Lizenzdateien, keine Hersteller-DLLs, keine Kundenprojekte, keine Zugangsdaten im Repository. `.gitignore` deckt `out/`, `*.ap21`, `*.zap21` außerhalb von `samples/`, Benutzerdateien von TIA und CODESYS ab. Secrets nie loggen.
- **Sprache**: Code, Bezeichner, Commit-Nachrichten Englisch; Meldungen an den Anwender Deutsch (Zielgruppe DACH), mit stabilem englischen Code davor (`[NOT_IN_OPENNESS_GROUP] Das Konto …`).
- **Abhängigkeiten** klein halten: .NET 10, `System.CommandLine`, `YamlDotNet`, `System.Text.Json`. Für SARIF reicht ein eigenes, kleines Modell.
- Nichts nach außen pushen oder veröffentlichen ohne ausdrückliche Anweisung.

## 13. Quellen

- Siemens Openness V21 Handbuch: https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/ (Seiten: Windows-Dienst, Benutzergruppe, Firewall, Compiling a project, Archiving and retrieving, Importing program block from document, What's new)
- Siemens SIMATIC SD Export/Import V21: https://docs.tia.siemens.cloud/r/en-us/v21/creating-and-managing-blocks/exporting-and-importing-blocks-in-simatic-sd-format-s7-1200-s7-1500-s7-1200-g2/exporting-and-importing-blocks-in-simatic-sd-format-s7-1200-s7-1500-s7-1200-g2
- Siemens TIA Portal AI Extensions (Skills): https://github.com/siemens/tia-portal-ai-extensions
- Siemens Openness Code Snippets V21: https://github.com/siemens/tia-portal-openness-code-snippets
- TIA Portal Test Suite Function Manual: https://cache.industry.siemens.com/dl/files/229/109825229/att_1157426/v2/TestSuiteOLH_enUS_en-US.pdf
- CERN, PLCSIM Advanced und Openness für Tests: https://accelconf.web.cern.ch/icalepcs2019/doi/JACoW-ICALEPCS2019-WEPHA018.html
- CODESYS Kommandozeile: https://content.helpme-codesys.com/en/CODESYS%20Development%20System/_cds_commandline.html
- CODESYS Static Analysis SARIF: https://content.helpme-codesys.com/en/CODESYS%20Static%20Analysis/_san_cmd_run_and_export_to_sarif_file.html
- CODESYS Test Manager Kommandozeile: https://content.helpme-codesys.com/en/CODESYS%20Test%20Manager/_tm_command_line_commands.html
- Beckhoff TE1200 Automation Interface: https://infosys.beckhoff.com/content/1031/te1200_tc3_plcstaticanalysis/6894897803.html
- Rockwell LOGIX-AT002 CI/CD: https://literature.rockwellautomation.com/idc/groups/literature/documents/at/logix-at002_-en-p.pdf
- Comquent Whitepaper „CI/CD für SPS-Projekte mit TIA Portal V21 und Jenkins“ (Oktober 2026), Zielarchitektur und Fallstricke
