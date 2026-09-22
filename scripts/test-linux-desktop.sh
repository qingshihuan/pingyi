#!/usr/bin/env bash
# Runs the real Avalonia application against the isolated Xvfb display; no OCR models/network.
set -euo pipefail
app="$PWD/src/PingYi.App/bin/Release/net10.0/PingYi.App.dll"
root="$(mktemp -d)"
export XDG_CONFIG_HOME="$root/config" XDG_DATA_HOME="$root/data" XDG_SESSION_TYPE=x11
export PINGYI_MODEL_DIR="$root/models" PINGYI_BUNDLED_MODEL_DIR="$root/no-bundled-models"
mkdir -p "$XDG_CONFIG_HOME/pingyi" "$PWD/artifacts/ui"
# Intentionally omit optional fields: this also covers real-process configuration migration.
cat > "$XDG_CONFIG_HOME/pingyi/settings.json" <<'JSON'
{"schemaVersion":9,"uiLanguage":"en-US","hotkey":"Ctrl+Alt+Shift+D","checkForUpdates":false}
JSON
openbox > "$root/wm.log" 2>&1 &
wm=$!
cleanup() {
  if [ -n "${app_pid:-}" ]; then
    kill "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  kill "$wm" 2>/dev/null || true
  wait "$wm" 2>/dev/null || true
  rm -rf "$root"
}
trap cleanup EXIT
sleep 0.5
dotnet "$app" --capture > "$root/app.log" 2>&1 &
app_pid=$!
diagnose() {
  echo "Synthetic desktop failure diagnostics ($1)" >&2
  cat "$root/app.log" "$root/wm.log" >&2 || true
  xwininfo -root -tree >&2 || true
  scrot "$PWD/artifacts/ui/native-linux-failure.png" || true
}
wait_window() {
  local name="$1"
  for i in $(seq 1 200); do
    if id=$(xdotool search --onlyvisible --name "^$name$" 2>/dev/null | head -n 1); then
      if [ -n "$id" ]; then echo "$id"; return 0; fi
    fi
    kill -0 "$app_pid" 2>/dev/null || { diagnose "app exited"; return 1; }
    sleep 0.1
  done
  echo "Expected a visible window: $name" >&2
  diagnose "$name"
  return 1
}
wait_no_overlay() {
  for i in $(seq 1 100); do
    if ! xdotool search --onlyvisible --name '^PingYi Capture$' >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.05
  done
  diagnose 'overlay remained visible after Escape'
  return 1
}
cancel_and_restore() {
  xdotool windowactivate --sync "$overlay" key Escape
  main=$(wait_window 'PingYi')
  wait_no_overlay
}
echo 'Testing cold-start --capture with partial settings'
overlay=$(wait_window 'PingYi Capture')
cancel_and_restore
echo 'Testing the real Start capture button'
xdotool windowactivate --sync "$main"
# Fixed English 1040x720 test window: a point inside the primary capture button.
# This is an actual pointer click, not a direct invocation of the coordinator.
scrot "$PWD/artifacts/ui/native-linux-main.png"
xdotool mousemove --window "$main" 200 238 click 1
overlay=$(wait_window 'PingYi Capture')
sleep 0.2
scrot "$PWD/artifacts/ui/native-linux-button-capture.png"
cancel_and_restore
echo 'Testing second-process --capture'
timeout 15s dotnet "$app" --capture
overlay=$(wait_window 'PingYi Capture')
cancel_and_restore
echo 'Testing registered X11 shortcut'
xdotool key --clearmodifiers ctrl+shift+d
overlay=$(wait_window 'PingYi Capture')
cancel_and_restore
echo 'Native desktop: partial settings, cold --capture, real button click, secondary --capture, registered hotkey, overlay visibility and Esc restoration passed.'

# Inherit the isolated synthetic desktop and exercise settings persistence.
source scripts/test-linux-hotkey-editor.sh
