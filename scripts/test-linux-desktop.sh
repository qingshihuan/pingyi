#!/usr/bin/env bash
# Private test bus and Xvfb only; never point this script at a user's real desktop.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "${1:-}" != "--inside" ]]; then
  dotnet build tests/PingYi.Linux.SmokeTests/PingYi.Linux.SmokeTests.csproj -c Release
  exec dbus-run-session -- xvfb-run -a -s "-screen 0 1280x720x24" bash "$0" --inside
fi
export PINGYI_PORTAL_TEST_DIR
PINGYI_PORTAL_TEST_DIR=$(mktemp -d)
export XDG_SESSION_TYPE=x11
unset WAYLAND_DISPLAY
/usr/bin/python3 tests/PingYi.Linux.SmokeTests/fake_portal.py &
portal_pid=$!
trap 'kill "$portal_pid" 2>/dev/null || true; wait "$portal_pid" 2>/dev/null || true; rm -rf "$PINGYI_PORTAL_TEST_DIR"' EXIT
for _ in {1..100}; do
  [[ -f "$PINGYI_PORTAL_TEST_DIR/ready" ]] && break
  kill -0 "$portal_pid" || exit 1
  sleep 0.05
done
test -f "$PINGYI_PORTAL_TEST_DIR/ready"
timeout 90s dotnet tests/PingYi.Linux.SmokeTests/bin/Release/net10.0/PingYi.Linux.SmokeTests.dll
