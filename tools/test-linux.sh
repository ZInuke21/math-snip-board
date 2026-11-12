#!/bin/bash
# Build with Mono and run every automated check headlessly (Linux/macOS; needs mono + Xvfb).
# Windows GDI+ behaves differently from Mono's libgdiplus - CI on windows-latest is the real test.
set -e
cd "$(dirname "$0")/.."
./build.sh
export DISPLAY=${DISPLAY:-:98}
pgrep -x Xvfb >/dev/null || (Xvfb "$DISPLAY" -screen 0 1600x1000x24 >/dev/null 2>&1 &) ; sleep 1
out=$(mktemp -d)
mono MathSnipBoard.exe --selftest "$out/selftest" | grep -E "PASS|FAIL|SELFTEST"
mono MathSnipBoard.exe --gdi-probe "$out/gdi-probe.txt" && tail -4 "$out/gdi-probe.txt"
mono MathSnipBoard.exe --parse-release tools/fixtures/github-release-latest.json "$out/parsed.txt" && cat "$out/parsed.txt"
echo "outputs in $out"
