#!/bin/bash
# Usage: ./package_macos.sh [--dmg] [version]

set -e
cd "$(dirname "$0")"

arch=$(uname -m)

if [[ $arch != "arm64" ]]; then
  echo "$arch not supported for macOS." >&2
  exit 1
fi

createDmg=false
version=""

for arg in "$@"; do
  if [[ $arg == "--dmg" ]]; then
    createDmg=true
  elif [[ $arg == -* ]]; then
    echo "Unknown option: $arg" >&2
    exit 1
  else
    version=$arg
  fi
done

if [[ -z $version ]]; then
  version=$(grep -m 1 "<AssemblyVersion>" ../Baballonia/Baballonia.csproj | sed 's/<[^>]*>//g' | tr -d '[:space:]')
fi

publishPath="bin/Release/net10.0/osx-arm64/publish"
appPath="bin/Baballonia.app"

if [[ ! -x "$publishPath/Baballonia.Desktop" ]]; then
  echo "You need to run dotnet publish before running this package script." >&2
  exit 1
fi

tmpDir=$(mktemp -d)
trap 'rm -rf "$tmpDir"' EXIT

tmpAppPath="$tmpDir/Baballonia.app"
mkdir -p "$tmpAppPath/Contents/MacOS" "$tmpAppPath/Contents/Resources"
cp -R "$publishPath/." "$tmpAppPath/Contents/MacOS/"

iconPath="Assets/Icon_512x512.png"
iconsetPath="$tmpDir/Baballonia.iconset"
mkdir "$iconsetPath"
sips -z 16 16 "$iconPath" --out "$iconsetPath/icon_16x16.png" > /dev/null
sips -z 32 32 "$iconPath" --out "$iconsetPath/icon_16x16@2x.png" > /dev/null
sips -z 32 32 "$iconPath" --out "$iconsetPath/icon_32x32.png" > /dev/null
sips -z 64 64 "$iconPath" --out "$iconsetPath/icon_32x32@2x.png" > /dev/null
sips -z 128 128 "$iconPath" --out "$iconsetPath/icon_128x128.png" > /dev/null
sips -z 256 256 "$iconPath" --out "$iconsetPath/icon_128x128@2x.png" > /dev/null
sips -z 256 256 "$iconPath" --out "$iconsetPath/icon_256x256.png" > /dev/null
sips -z 512 512 "$iconPath" --out "$iconsetPath/icon_256x256@2x.png" > /dev/null
sips -z 512 512 "$iconPath" --out "$iconsetPath/icon_512x512.png" > /dev/null
iconutil -c icns "$iconsetPath" -o "$tmpAppPath/Contents/Resources/Baballonia.icns"

cat > "$tmpAppPath/Contents/Info.plist" <<EOF
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
    <string>$version</string>
    <key>CFBundleVersion</key>
    <string>$version</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSCameraUsageDescription</key>
    <string>Cameras are used for eye and face tracking.</string>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$tmpAppPath"

rm -rf "$appPath"
mv "$tmpAppPath" "$appPath"

echo "Built $appPath ($version)"

if [[ $createDmg == true ]]; then
  dmgPath="bin/Baballonia.dmg"
  dmgStagingPath="$tmpDir/dmg"

  ditto "$appPath" "$dmgStagingPath/Baballonia.app"
  ln -s /Applications "$dmgStagingPath/Applications"
  hdiutil create -quiet -volname "Baballonia" -srcfolder "$dmgStagingPath" -ov -format UDZO "$dmgPath"

  echo "Built $dmgPath"
fi
