#!/usr/bin/env bash
#
# Build a VibeSkua Linux AppImage via Velopack.
#
# Usage:  ./packaging/build-appimage.sh [version] [--mock]
#   version   semver for the release (default: 0.1.0)
#   --mock    bundle the offline mock Flash backend instead of embedded Ruffle
#             (default is the real ruffle_core, which needs a nightly Rust
#             toolchain + network to compile).
#
# Requirements: .NET 10 SDK, Rust (nightly for the default/real runtime),
#               squashfs-tools, and the `vpk` global tool
#               (`dotnet tool install -g vpk`).
#
# Output: packaging/Releases/VibeSkuaLinux.AppImage (+ Velopack update metadata).
set -euo pipefail

VERSION="${1:-0.1.0}"
RUFFLE=true
[[ "${2:-}" == "--mock" || "${1:-}" == "--mock" ]] && RUFFLE=false
[[ "${1:-}" == "--mock" ]] && VERSION="0.1.0"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="$ROOT/packaging/publish"
RELEASE_DIR="$ROOT/packaging/Releases"
ICON="$ROOT/packaging/icon.png"

echo ">> VibeSkua Linux $VERSION  (ruffle=$RUFFLE)"

rm -rf "$PUBLISH_DIR"
dotnet publish "$ROOT/Skua.Avalonia/Skua.Avalonia.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:SkuaRuffle=$RUFFLE \
    -o "$PUBLISH_DIR"

# Ensure the vpk tool is reachable even when installed into the dotnet tools dir.
export PATH="$PATH:$HOME/.dotnet/tools"

vpk pack \
    --packId VibeSkuaLinux \
    --packTitle "VibeSkua Linux" \
    --packVersion "$VERSION" \
    --packDir "$PUBLISH_DIR" \
    --mainExe Skua.Avalonia \
    --icon "$ICON" \
    --outputDir "$RELEASE_DIR"

echo ">> Done: $RELEASE_DIR/VibeSkuaLinux.AppImage"
