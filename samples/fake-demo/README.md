# fake-demo

Beispiel-Repository für plc-build mit dem Fake-Adapter (Meilenstein M0). Braucht nur das .NET 10 SDK, keine Engineering-Software.

- `plc.yml`: ein Projekt `linie-1/plc` mit Adapter `fake`
- `linie-1/plc/base/Linie1.zapfake`: Platzhalter für das Basisarchiv
- `linie-1/plc/src/plc/`: Platzhalter für Textquellen
- `Jenkinsfile`: Pipeline nach CLAUDE.md Abschnitt 11, läuft auf jedem Agenten mit .NET 10 SDK
- `run-pipeline.sh`: dieselben Schritte lokal ohne Jenkins

Das Szenario steuert die Umgebungsvariable `PLC_BUILD_FAKE_SCENARIO` (in Jenkins: Parameter `SCENARIO`) oder das Feld `fake.scenario` in `plc.yml`:

| Szenario | Schritt | Exit-Code |
|---|---|---|
| `ok` | alle | 0 |
| `compile-error` | compile | 1 |
| `rule-violation` | analyze | 1 |
| `test-failure` | test | 1 |
| `license-missing` | alle, auch doctor | 3 |
| `hang` | alle außer doctor | 4 nach Zeitlimit |
| `crash`, `no-response`, `invalid-response`, `bad-protocol` | alle außer doctor | 4 |

`PLC_BUILD_FAKE_STEP=<schritt>` beschränkt das Szenario auf einen Schritt. `PLC_BUILD_FAKE_SPAWN_HELPER=1` lässt den Adapter einen Hilfsprozess zurücklassen, den `plc-build cleanup` beendet.

Hinweis: Der Adapter wird als native Startdatei (apphost) aufgerufen. Liegt .NET nicht am Standardort, muss `DOTNET_ROOT` gesetzt sein.
