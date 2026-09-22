#!/usr/bin/env bash
# Real Avalonia processes on an isolated Xvfb/Openbox desktop; no user data, OCR models or network.
set -euo pipefail
app="$PWD/src/PingYi.App/bin/Release/net10.0/PingYi.App.dll"
root="$(mktemp -d)"
export XDG_CONFIG_HOME="$root/config" XDG_DATA_HOME="$root/data" XDG_SESSION_TYPE=x11
export PINGYI_MODEL_DIR="$root/models" PINGYI_BUNDLED_MODEL_DIR="$root/no-bundled-models"
mkdir -p "$XDG_CONFIG_HOME/pingyi" "$PWD/artifacts/ui"
# Real-process upgrade from the v0.5.1 default, intentionally omitting optional fields.
cat > "$XDG_CONFIG_HOME/pingyi/settings.json" <<'JSON'
{"schemaVersion":9,"uiLanguage":"en-US","hotkey":"Ctrl+Alt+Shift+D","checkForUpdates":false}
JSON
openbox > "$root/wm.log" 2>&1 &
wm=$!
cleanup() {
  if [ -n "${app_pid:-}" ]; then kill "$app_pid" 2>/dev/null || true; wait "$app_pid" 2>/dev/null || true; fi
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
    kill -0 "$app_pid" 2>/dev/null || { diagnose 'app exited'; return 1; }
    sleep 0.1
  done
  diagnose "Expected a visible window: $name"; return 1
}
wait_no_overlay() {
  for i in $(seq 1 100); do
    if ! xdotool search --onlyvisible --name '^PingYi Capture$' >/dev/null 2>&1; then return 0; fi
    sleep 0.05
  done
  diagnose 'overlay remained visible'; return 1
}
cancel_and_restore() {
  xdotool windowactivate --sync "$overlay" key Escape
  main=$(wait_window 'PingYi')
  wait_no_overlay
}
wait_saved_hotkey() {
  local expected="$1"
  for i in $(seq 1 100); do
    if /usr/bin/python3 - "$XDG_CONFIG_HOME/pingyi/settings.json" "$expected" <<'PY'
import json, sys
try:
    settings = json.load(open(sys.argv[1]))
    assert settings['hotkey'] == sys.argv[2]
    assert settings['schemaVersion'] == 10
except (OSError, ValueError, KeyError, AssertionError):
    sys.exit(1)
PY
    then return 0; fi
    sleep 0.1
  done
  diagnose "Shortcut was not saved: $expected"; return 1
}
open_hotkey_settings() {
  timeout 15s dotnet "$app" --settings
  settings=$(wait_window 'PingYi Settings')
  xdotool windowsize "$settings" 1040 760 windowactivate --sync "$settings"
  # Fixed English 1040x760 settings window: Appearance & startup category.
  xdotool mousemove --window "$settings" 110 323 click 1
  sleep 0.4
}
echo 'Testing cold-start --capture with a legacy default and partial settings'
overlay=$(wait_window 'PingYi Capture'); cancel_and_restore
echo 'Testing the real Start capture button'
xdotool windowactivate --sync "$main"
scrot "$PWD/artifacts/ui/native-linux-main.png"
xdotool mousemove --window "$main" 200 238 click 1
overlay=$(wait_window 'PingYi Capture'); sleep 0.2
scrot "$PWD/artifacts/ui/native-linux-button-capture.png"
cancel_and_restore
echo 'Testing second-process --capture'
timeout 15s dotnet "$app" --capture
overlay=$(wait_window 'PingYi Capture'); cancel_and_restore
echo 'Testing the migrated Ctrl+Shift+D X11 shortcut'
xdotool key --clearmodifiers ctrl+shift+d
overlay=$(wait_window 'PingYi Capture'); cancel_and_restore

echo 'Testing the actual shortcut recorder and Apply button'
open_hotkey_settings
scrot "$PWD/artifacts/ui/native-linux-hotkey-editor.png"
xdotool mousemove --window "$settings" 350 398 click 1
sleep 0.3
xdotool key --clearmodifiers ctrl+shift+g
sleep 0.3
xdotool mousemove --window "$settings" 665 398 click 1
wait_saved_hotkey 'Ctrl+Shift+G'
scrot "$PWD/artifacts/ui/native-linux-custom-hotkey.png"
xdotool windowclose "$settings"
sleep 0.2
xdotool key --clearmodifiers ctrl+shift+g
overlay=$(wait_window 'PingYi Capture'); cancel_and_restore
xdotool key --clearmodifiers ctrl+shift+d
sleep 0.4
if xdotool search --onlyvisible --name '^PingYi Capture$' >/dev/null 2>&1; then
  diagnose 'old shortcut was not released'; exit 1
fi

echo 'Testing custom shortcut persistence across an actual process restart'
kill "$app_pid"; wait "$app_pid" 2>/dev/null || true
sleep 0.2
dotnet "$app" > "$root/app.log" 2>&1 &
app_pid=$!
main=$(wait_window 'PingYi')
sleep 0.3
xdotool key --clearmodifiers ctrl+shift+g
overlay=$(wait_window 'PingYi Capture'); cancel_and_restore

echo 'Testing Esc cancellation and restoring the Linux default'
open_hotkey_settings
xdotool mousemove --window "$settings" 350 398 click 1
sleep 0.3
xdotool key Escape
sleep 0.3
# Restore default changes only the draft; Apply performs registration and persistence.
xdotool mousemove --window "$settings" 515 398 click 1
xdotool mousemove --window "$settings" 665 398 click 1
wait_saved_hotkey 'Ctrl+Shift+D'
xdotool windowclose "$settings"
sleep 0.2
xdotool key --clearmodifiers ctrl+shift+d
overlay=$(wait_window 'PingYi Capture'); cancel_and_restore
echo 'Native desktop passed: migration, button capture, cold/secondary --capture, recorder, rebind, old-grab release, persistence after restart, Esc cancellation and default restoration.'
