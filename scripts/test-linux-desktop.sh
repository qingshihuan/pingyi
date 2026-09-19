#!/usr/bin/env bash
# A real Avalonia/X11 smoke test on an isolated Xvfb desktop. No private desktop data or model downloads.
set -euo pipefail
app="$PWD/src/PingYi.App/bin/Release/net10.0/PingYi.App.dll"
root="$(mktemp -d)"
export XDG_CONFIG_HOME="$root/config" XDG_DATA_HOME="$root/data"
export PINGYI_MODEL_DIR="$root/models" PINGYI_BUNDLED_MODEL_DIR="$root/bundled"
mkdir -p "$XDG_CONFIG_HOME/pingyi"
printf '%s\n' '{"schemaVersion":9,"uiLanguage":"en-US","hotkey":"Ctrl+Alt+Shift+D","checkForUpdates":false}' > "$XDG_CONFIG_HOME/pingyi/settings.json"
openbox > "$root/window-manager.log" 2>&1 &
wm=$!
app_pid=''
cleanup() {
  test -z "$app_pid" || kill "$app_pid" 2>/dev/null || true
  kill "$wm" 2>/dev/null || true
  rm -rf "$root"
}
trap cleanup EXIT
sleep 0.5
dotnet "$app" --capture > "$root/app.log" 2>&1 &
app_pid=$!
wait_window() {
  local name="$1"
  for i in $(seq 1 200); do
    kill -0 "$app_pid" 2>/dev/null || { cat "$root/app.log"; return 1; }
    if window=$(xdotool search --onlyvisible --name "^$name$" 2>/dev/null | head -1) && test -n "$window"; then
      printf '%s' "$window"
      return 0
    fi
    sleep 0.1
  done
  cat "$root/app.log" >&2
  echo "Expected a visible window: $name" >&2
  return 1
}
overlay=$(wait_window 'PingYi Capture')
xdotool windowactivate --sync "$overlay" key Escape
main=$(wait_window 'PingYi')
# Secondary --capture must trigger the existing app too, including when not focused.
dotnet "$app" --capture
overlay=$(wait_window 'PingYi Capture')
xdotool windowactivate --sync "$overlay" key Escape
main=$(wait_window 'PingYi')
# The registered shortcut enters the very same real selection UI.
xdotool key --clearmodifiers ctrl+alt+shift+d
overlay=$(wait_window 'PingYi Capture')
xdotool windowactivate --sync "$overlay" key Escape
wait_window 'PingYi' > /dev/null
printf '%s\n' 'Native desktop: cold --capture, secondary --capture, registered hotkey, overlay visibility and Esc restoration passed.'
