# Live-Tests

Tests gegen echte Engineering-Software. Sie laufen nur mit `PLC_BUILD_LIVE=1` auf dem Windows-Agenten und immer über den Adapter-Prozess.

Regeln (CLAUDE.md Abschnitt 12):

- Jeder Live-Lauf prüft, dass mehr als 0 Tests ausgeführt wurden.
- Jeder Live-Lauf prüft, dass danach kein von ihm gestarteter Prozess übrig ist.
- Messwerte nach jedem Lauf in `docs/messungen.md`.

Kommt mit M1 (TIA Portal V21).
