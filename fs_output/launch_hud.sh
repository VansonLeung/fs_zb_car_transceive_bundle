#!/usr/bin/env bash
set -euo pipefail

URL="http://localhost:5080"
FLAGS=("--app=${URL}" "--use-fake-ui-for-media-stream")

launch() {
  "$@" "${FLAGS[@]}" &
  exit 0
}

# macOS app bundles
if [[ -x "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge" ]]; then
  launch "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
fi
if [[ -x "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" ]]; then
  launch "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
fi

# Linux binaries
for bin in microsoft-edge microsoft-edge-stable google-chrome chrome chromium chromium-browser; do
  if command -v "$bin" >/dev/null 2>&1; then
    launch "$bin"
  fi
done

echo "No supported browser (Edge/Chrome/Chromium) found in PATH." >&2
exit 1
