#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PUBLISH_DIR="$REPO_DIR/artifacts/publish/osx-arm64"
APP_DIR="$REPO_DIR/artifacts/BusDockStudio.app"

dotnet publish "$REPO_DIR/src/BusDockStudio.Desktop/BusDockStudio.Desktop.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  --output "$PUBLISH_DIR" \
  -p:PublishSingleFile=false

mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
cp "$REPO_DIR/build/Info.plist" "$APP_DIR/Contents/Info.plist"
chmod +x "$APP_DIR/Contents/MacOS/BusDockStudio"

echo "Created $APP_DIR"
