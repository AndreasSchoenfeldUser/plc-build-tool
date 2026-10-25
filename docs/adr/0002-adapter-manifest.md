# 0002 Adapter-Beschreibung in adapter.json

Stand: 25.10.2026, angenommen

## Kontext

Der Kern muss wissen, wie er einen Adapter startet, welcher Abschnitt aus `plc.yml` dazugehört und welche Eingänge ins Build-Manifest gehören. Stünde das im Kern, gäbe es dort Verzweigungen je Hersteller (gegen H3). Bei CODESYS ist die Exe außerdem keine Adapter-Datei, sondern `CODESYS.exe` aus `plc.yml`.

## Entscheidung

Jeder Adapter liegt in einem eigenen Verzeichnis mit `adapter.json`:

```json
{
  "name": "fake",
  "protocol": 1,
  "command": "{adapterDir}/PlcBuild.Adapters.Fake",
  "args": ["{request}"],
  "settingsKey": "fake",
  "manifestInputs": ["baseArchive", "sources"],
  "secrets": {}
}
```

- Platzhalter: `{adapterDir}`, `{request}`, `{projectRoot}`, `{settings.<feld>}`. Fehlt ein benötigtes Feld in `plc.yml`, endet der Aufruf mit Exit 2.
- `settingsKey`: dieser Abschnitt des Projekts in `plc.yml` geht als `project.settings` in den Request.
- `manifestInputs`: Felder aus `settings`, die Dateien oder Verzeichnisse benennen. Der Kern prüft vor dem Start, dass sie existieren (sonst Exit 2, `INPUT_NOT_FOUND`), und hasht sie ins Manifest.
- `secrets`: Namen von Umgebungsvariablen, die der Adapter lesen darf, z. B. für UMAC. Nur die Namen gehen in den Request.
- Fundort: `<Verzeichnis von plc-build>/adapters/<name>/`, überschreibbar mit `PLC_BUILD_ADAPTER_<NAME>` (Großbuchstaben, `-` wird `_`, z. B. `PLC_BUILD_ADAPTER_TIA_V21`).
- Fehlt der Adapter oder passt seine Protokollversion nicht, ist das ein Umgebungsfehler (Exit 3).

Erwarteter Eintrag für CODESYS (M3, noch nicht geprüft):

```json
{ "command": "{settings.exe}",
  "args": ["--profile={settings.profile}", "--noUI", "--runscript={adapterDir}/adapter.py", "--scriptargs={request}"],
  "settingsKey": "codesys" }
```

## Folgen

- Der Kern enthält keinen Herstellernamen. Ein neuer Adapter braucht keine Kern-Änderung.
- `adapter.json` gehört zum Adapter-Paket und wird mit ihm verteilt.
