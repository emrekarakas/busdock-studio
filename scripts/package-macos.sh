#!/usr/bin/env zsh
set -euo pipefail

script_dir="${0:A:h}"
repo_dir="${script_dir:h}"
runtime_id="${1:-osx-arm64}"
version="${VERSION:-0.1.0}"
build_number="${BUILD_NUMBER:-1}"
publish_dir="$repo_dir/artifacts/publish/$runtime_id-release"
app_dir="$repo_dir/artifacts/BusDock Studio.app"
dmg_path="$repo_dir/artifacts/BusDockStudio-$version-macos-${runtime_id#osx-}.dmg"
stage_dir="$(mktemp -d "${TMPDIR:-/tmp}/busdock-dmg.XXXXXX")"

cleanup() {
  rm -rf "$stage_dir"
}
trap cleanup EXIT

rm -rf "$publish_dir" "$app_dir"
mkdir -p "$publish_dir" "$app_dir/Contents/MacOS"

dotnet publish "$repo_dir/src/BusDockStudio.Desktop/BusDockStudio.Desktop.csproj" \
  --configuration Release \
  --runtime "$runtime_id" \
  --self-contained true \
  --output "$publish_dir" \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -p:DebugSymbols=false

ditto "$publish_dir" "$app_dir/Contents/MacOS"
cp "$repo_dir/build/Info.plist" "$app_dir/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app_dir/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $build_number" "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/BusDockStudio"

codesign --force --deep --sign - "$app_dir"
codesign --verify --deep --strict --verbose=2 "$app_dir"

ditto "$app_dir" "$stage_dir/BusDock Studio.app"
ln -s /Applications "$stage_dir/Applications"
hdiutil create \
  -volname "BusDock Studio" \
  -srcfolder "$stage_dir" \
  -format UDZO \
  -ov \
  "$dmg_path"
hdiutil verify "$dmg_path"

echo "$dmg_path"
