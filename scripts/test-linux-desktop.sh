#!/usr/bin/env bash
# Runs the real Avalonia application against the isolated Xvfb display; no OCR models/network.
set -euo pipefail
app="$PWD/src/PingYi.App/bin/Release/net10.0/PingYi.App.dll"
root="$(mktemp -d)"
export XDG_CONFIG_HOME="$root/config" XDG_DATA_HOME="$root/data" XDG_SESSION_TYPE=x11
export PINGYI_MODEL_DIR="$root/models" PINGYI_BUNDLED_MODEL_DIR="$root/no-bundled-models"
mkdir -p "$XDG_CONFIG_HOME/pingyi" "$PWD/artifacts/ui"
cat > "$XDG_CONFIG_HOME/pingyi/settings.json" <<'JSON'
{"schemaVersion":9,"uiLanguage":"en-US","hotkey":"Ctrl+Alt+Shift+D","checkForUpdates":false}
JSON
openbox > "$root/wm.log" 2>&1 &
wm=$!
trap 'kill ${app_pid:-0} "$wm" 2>/dev/null || true; rm -rf "$root"' EXIT
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
echo 'Testing cold-start --capture'
overlay=$(wait_window 'PingYi Capture')
xdotool windowactivate --sync "$overlay" key Escape
main=$(wait_window 'PingYi')
echo 'Testing second-process --capture'
dotnet "$app" --capture
overlay=$(wait_window 'PingYi Capture')
xdotool windowactivate --sync "$overlay" key Escape
main=$(wait_window 'PingYi')
echo 'Testing registered X11 shortcut'
xdotool key --clearmodifiers ctrl+alt+shift+d
overlay=$(wait_window 'PingYi Capture')
xdotool windowactivate --sync "$overlay" key Escape
wait_window 'PingYi' >/dev/null
echo 'Native desktop: cold --capture, secondary --capture, registered hotkey, overlay visibility and Esc restoration passed.'
