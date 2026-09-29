#!/usr/bin/env bash
# Runs the real Avalonia application against the isolated Xvfb display; no OCR models/network.
set -euo pipefail
app="$PWD/src/PingYi.App/bin/Release/net10.0/PingYi.App.dll"
root="$(mktemp -d)"
export XDG_CONFIG_HOME="$root/config" XDG_DATA_HOME="$root/data" XDG_SESSION_TYPE=x11
export PINGYI_MODEL_DIR="$root/models" PINGYI_BUNDLED_MODEL_DIR="$root/no-bundled-models"
mkdir -p "$XDG_CONFIG_HOME/pingyi-complete" "$PWD/artifacts/ui"
# Omit the new automatic-task flag too: migrate the legacy default, retaining other preferences.
cat > "$XDG_CONFIG_HOME/pingyi-complete/settings.json" <<'JSON'
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
    if ! xdotool search --onlyvisible --name '^Screen Insight Capture$' >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.05
  done
  diagnose 'overlay remained visible after Escape'
  return 1
}
cancel_and_restore() {
  xdotool windowactivate --sync "$overlay" key Escape
  main=$(wait_window 'Screen Insight Complete')
  wait_no_overlay
}
echo 'Testing cold-start --capture with partial settings'
overlay=$(wait_window 'Screen Insight Capture')
cancel_and_restore
echo 'Testing the real Smart capture primary button'
xdotool windowactivate --sync "$main"
# Verified against native-linux-main.png at 1040x720: inside the blue primary button,
# not the manual action row below it. This is an actual pointer click.
scrot "$PWD/artifacts/ui/native-linux-main.png"
xdotool mousemove --window "$main" 200 196 click 1
overlay=$(wait_window 'Screen Insight Capture')
sleep 0.2
scrot "$PWD/artifacts/ui/native-linux-button-capture.png"
cancel_and_restore
echo 'Testing second-process --capture'
timeout 15s dotnet "$app" --capture
overlay=$(wait_window 'Screen Insight Capture')
cancel_and_restore
echo 'Testing registered X11 shortcut after schema-9 migration'
xdotool key --clearmodifiers ctrl+shift+d
overlay=$(wait_window 'Screen Insight Capture')
cancel_and_restore
echo 'Native desktop: partial settings, cold --capture, real primary button, secondary --capture, registered hotkey, overlay visibility and Esc restoration passed.'


echo 'Testing explicit Quit and release resources (not close-to-tray)'
# Record only the synthetic application's existing direct children, never look up by executable name.
children=$(pgrep -P "$app_pid" || true)
xdotool windowactivate --sync "$main"
scrot "$PWD/artifacts/ui/native-linux-before-quit.png"
xdotool mousemove --window "$main" 900 683 click 1
for i in $(seq 1 240); do
  kill -0 "$app_pid" 2>/dev/null || break
  sleep 0.1
done
if kill -0 "$app_pid" 2>/dev/null; then
  diagnose 'application did not finish explicit exit'
  exit 1
fi
wait "$app_pid"
app_pid=''
for child in $children; do
  if test -e "/proc/$child/stat"; then
    state=$(awk '{print $3}' "/proc/$child/stat")
    if test "$state" != Z; then
      diagnose 'an owned synthetic backend survived exit'
      exit 1
    fi
  fi
done
echo 'Explicit exit completed and the synthetic application children are no longer running.'
