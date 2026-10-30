#!/bin/bash
# Usage: ./package_macos.sh [--dmg] [version]
set -euo pipefail
cd "$(dirname "$0")"

arch=$(uname -m)

if [[ $arch != "arm64" ]]; then
  echo "$arch not supported for macOS." >&2
  exit 1
fi

DMG=false
VERSION=""
for arg in "$@"; do
  case $arg in
    --dmg) DMG=true ;;
    -*) echo "Unknown option: $arg" >&2; exit 1 ;;
    *) VERSION=$arg ;;
  esac
done

VERSION="${VERSION:-$(sed -n 's:.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*:\1:p' ../Baballonia/Baballonia.csproj | head -1)}"
PUBLISH="bin/Release/net10.0/osx-arm64/publish"
APP="bin/Baballonia.app"

if [ ! -x "$PUBLISH/Baballonia.Desktop" ]; then
  echo "You need to run dotnet publish before running this package script." >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT
trap 'rm -rf "$APP"' ERR
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

ICONSET="$TMP_DIR/Baballonia.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s Assets/Icon_512x512.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  [ $d -le 512 ] && sips -z $d $d Assets/Icon_512x512.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Baballonia.icns"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Baballonia</string>
    <key>CFBundleDisplayName</key>
    <string>Baballonia</string>
    <key>CFBundleIdentifier</key>
    <string>diy.babble.baballonia</string>
    <key>CFBundleExecutable</key>
    <string>Baballonia.Desktop</string>
    <key>CFBundleIconFile</key>
    <string>Baballonia.icns</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSCameraUsageDescription</key>
    <string>Cameras are used for eye and face tracking.</string>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
trap - ERR

echo "Built $APP ($VERSION)"

if [[ $DMG == true ]]; then
  DMG_PATH="bin/Baballonia.dmg"
  STAGING="$TMP_DIR/dmg"
  ditto "$APP" "$STAGING/Baballonia.app"
  ln -s /Applications "$STAGING/Applications"
  hdiutil create -quiet -volname "Baballonia" -srcfolder "$STAGING" -ov -format UDZO "$DMG_PATH"
  echo "Built $DMG_PATH"
fi
