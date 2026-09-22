#!/usr/bin/env bash
# Sourced by test-linux-desktop.sh: inherits its isolated paths, app pid and diagnostics.
set -euo pipefail
wait_saved_hotkey() {
  local expected="$1"
  for i in $(seq 1 100); do
    if /usr/bin/python3 - "$XDG_CONFIG_HOME/pingyi/settings.json" "$expected" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8') as stream:
    value = json.load(stream)
sys.exit(0 if value.get('hotkey') == sys.argv[2] and value.get('schemaVersion') == 10 else 1)
PY
    then return 0; fi
    sleep 0.1
  done
  diagnose 'shortcut was not persisted'
  return 1
}
open_shortcut_settings() {
  timeout 15s dotnet "$app" --settings
  settings=$(wait_window 'PingYi Settings')
  xdotool windowactivate --sync "$settings"
  xdotool mousemove --window "$settings" 100 323 click 1
  sleep 0.3
}
open_recorder() {
  # Focus the shortcut editor, then use actual keyboard navigation to Record shortcut.
  xdotool mousemove --window "$settings" 450 350 click 1 key Tab key space
  recorder=$(wait_window 'Record capture shortcut')
  xdotool windowactivate --sync "$recorder"
}
echo 'Testing recording the active shortcut without triggering capture'
open_shortcut_settings
open_recorder
scrot "$PWD/artifacts/ui/native-linux-shortcut-recorder.png"
xdotool key --clearmodifiers ctrl+shift+d
settings=$(wait_window 'PingYi Settings')
wait_no_overlay

echo 'Testing recording a custom shortcut and applying it'
open_recorder
xdotool key --clearmodifiers ctrl+alt+g
settings=$(wait_window 'PingYi Settings')
xdotool windowactivate --sync "$settings" key --clearmodifiers ctrl+s
wait_saved_hotkey 'Ctrl+Alt+G'
sleep 0.2
scrot "$PWD/artifacts/ui/native-linux-custom-shortcut.png"
xdotool key --clearmodifiers ctrl+shift+d
sleep 0.3
wait_no_overlay
xdotool key --clearmodifiers ctrl+alt+g
overlay=$(wait_window 'PingYi Capture')
cancel_and_restore

echo 'Testing a full process restart with the saved shortcut'
kill "$app_pid"
wait "$app_pid" 2>/dev/null || true
dotnet "$app" >> "$root/app.log" 2>&1 &
app_pid=$!
main=$(wait_window 'PingYi')
sleep 0.4
xdotool key --clearmodifiers ctrl+alt+g
overlay=$(wait_window 'PingYi Capture')
cancel_and_restore

echo 'Testing Restore default and saved platform preference'
open_shortcut_settings
# Text field -> Record shortcut -> Restore default.
xdotool mousemove --window "$settings" 450 350 click 1 key Tab key Tab key space
xdotool key --clearmodifiers ctrl+s
wait_saved_hotkey 'Ctrl+Shift+D'
/usr/bin/python3 - "$XDG_CONFIG_HOME/pingyi/settings.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8') as stream:
    assert json.load(stream).get('hotkeyIsCustomized') is False
PY
xdotool key --clearmodifiers ctrl+shift+d
overlay=$(wait_window 'PingYi Capture')
cancel_and_restore
echo 'Native hotkey editor: active-key recording, custom apply, old-key release, restart persistence and restore default passed.'
