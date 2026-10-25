# 0004 `--out` beim Befehl archive

Stand: 25.10.2026, angenommen

## Kontext

CLAUDE.md Abschnitt 4 nennt `--out <dir>` als gemeinsame Option für das Ausgabeverzeichnis und zugleich `archive --out <file>` für die Archivdatei.

## Entscheidung

Bei `archive` bezeichnet `--out` die Archivdatei, wie im Jenkinsfile-Muster (`--out out\Linie1_%BUILD_NUMBER%.zap21`). Das Ausgabeverzeichnis heißt dort `--out-dir` (Vorgabe `out/`). Alle anderen Befehle verwenden `--out` für das Ausgabeverzeichnis.

## Folgen

Das Jenkinsfile-Muster bleibt unverändert gültig. Wer bei `archive` ein anderes Ausgabeverzeichnis braucht, muss `--out-dir` angeben.
