#!/usr/bin/env bash
# Runs the steps of the Jenkinsfile locally, without Jenkins.
# Usage: samples/fake-demo/run-pipeline.sh [scenario]     (from the repository root)
set -u
cd "$(dirname "$0")"
export PLC_BUILD_FAKE_SCENARIO="${1:-ok}"
dotnet build ../../src/PlcBuild.Cli -c Release -v quiet -nologo >/dev/null || exit 2
plc() { dotnet ../../src/PlcBuild.Cli/bin/Release/net10.0/plc-build.dll "$@"; }

P=linie-1/plc
rm -rf out
status=0
for step in doctor restore import compile analyze test; do
  args=(--project "$P")
  [ "$step" != doctor ] && args+=(--timeout 0.2)
  plc "$step" "${args[@]}"; rc=$?
  echo "== $step: Exit $rc"
  if [ $rc -ne 0 ]; then status=$rc; break; fi
done
[ $status -eq 0 ] && plc archive --project "$P" --out out/Linie1_local.zapfake
plc report --project "$P" --junit out/junit --sarif out/sarif
plc cleanup || true
echo "Ergebnis: Exit $status"
exit $status
