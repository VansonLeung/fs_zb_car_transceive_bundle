#!/usr/bin/env bash
set -euo pipefail

# macOS publish helper for the web HUD
# Note: the DirectX ground app and WinForms launcher are Windows-only and are not built here.

ROOT="/Users/van/Documents/projects/fs/fs_zb_car_transceive_bundle"
RID="osx-x64"
if [[ "$(uname -m)" == "arm64" ]]; then
  RID="osx-arm64"
fi

OUT_GUI="${ROOT}/fs_output/rc_gui_osx"
mkdir -p "$OUT_GUI"

cd "${ROOT}/fs_zb_car_transceive_bundle/fs_zb_serial_gnd_app_win10_net_webapp_gui"
dotnet publish -c Release -r "$RID" --self-contained true /p:PublishSingleFile=true -o "$OUT_GUI"

echo "Done. Web GUI published to: $OUT_GUI"