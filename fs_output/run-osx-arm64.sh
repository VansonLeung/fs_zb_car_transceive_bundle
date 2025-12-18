#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "$0")" && pwd)/osx-arm64"

HID="$ROOT/rc_hid_monitor/HIDDeviceMonitor"
GND="$ROOT/rc_gnd/RCCarController"
GUI_DIR="$ROOT/rc_gui"
GUI="$GUI_DIR/fs_zb_serial_gnd_app_win10_net_webapp_gui"
LAUNCHER="$ROOT/../launch_hud.sh"

kill_if_running() {
  local name="$1"
  if pgrep -f "$name" >/dev/null 2>&1; then
    echo "Stopping existing $name..."
    pkill -f "$name" || true
    sleep 1
  fi
}

run_app() {
  local app="$1"
  local label="$2"
  local workdir="$3"
  shift 3 || true
  local cmd=("$app" "$@")
  if [[ -x "$app" ]]; then
    echo "Starting $label..."
    (
      cd "$workdir" || exit 1
      "${cmd[@]}" &
    )
  elif [[ -e "$app" ]]; then
    echo "$label exists but is not executable: $app"
  else
    echo "$label not found at $app"
  fi
}
  kill_if_running "HIDDeviceMonitor"

run_app "$HID" "HID monitor" "$ROOT"
sleep 5
run_app "$GND" "Ground app" "$ROOT"
sleep 5
ASPNETCORE_CONTENTROOT="$GUI_DIR" run_app "$GUI" "Web GUI" "$GUI_DIR"
sleep 5
if [[ -x "$LAUNCHER" ]]; then
  ASPNETCORE_CONTENTROOT="$ROOT/rc_gui" "$LAUNCHER" &
else
  echo "GUI Launcher script not found at $LAUNCHER"
fi

echo "Done."
