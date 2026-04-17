#!/bin/bash
# ─────────────────────────────────────────────────────────────────────────────
# FishSynth Mac Build → Sign → Notarize → Staple → Zip
#
# Usage:
#   1. Build the .app from Unity (File → Build Settings → Build)
#   2. Run: ./build-mac.sh path/to/FishSynth.app
#   3. Wait. When done, you'll have a signed+notarized zip ready to ship.
#
# One-time setup (already done if you followed the guide):
#   - Developer ID Application cert in Keychain
#   - App-specific password stored via: xcrun notarytool store-credentials
#   - entitlements.plist in the same folder as this script
# ─────────────────────────────────────────────────────────────────────────────

set -e  # exit on any error

# ── Edit these for your account ────────────────────────────────────────────
SIGN_IDENTITY="Developer ID Application: David Carney (39BDSL9QEF)"
KEYCHAIN_PROFILE="fishsynth-notary"
# Entitlements resolved at runtime: looks next to the .app first, then next to this script
# ───────────────────────────────────────────────────────────────────────────

# ── Argument check ─────────────────────────────────────────────────────────
if [ -z "$1" ]; then
    echo "Usage: $0 <path-to-app>"
    echo "Example: $0 ~/Builds/FishSynth_v1/FishSynth_v1.app"
    exit 1
fi

APP_PATH="$1"
APP_NAME=$(basename "$APP_PATH" .app)
APP_DIR=$(dirname "$APP_PATH")
ZIP_PATH="$APP_DIR/${APP_NAME}.zip"
FINAL_ZIP="$APP_DIR/${APP_NAME}-signed.zip"

if [ ! -d "$APP_PATH" ]; then
    echo "❌ App not found: $APP_PATH"
    exit 1
fi

# Look for entitlements.plist: next to the .app first, then next to this script
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$APP_DIR/entitlements.plist" ]; then
    ENTITLEMENTS="$APP_DIR/entitlements.plist"
elif [ -f "$SCRIPT_DIR/entitlements.plist" ]; then
    ENTITLEMENTS="$SCRIPT_DIR/entitlements.plist"
else
    echo "❌ entitlements.plist not found."
    echo "   Looked in: $APP_DIR/"
    echo "   And:       $SCRIPT_DIR/"
    exit 1
fi
echo "Using entitlements: $ENTITLEMENTS"

# ── 1. Sign ────────────────────────────────────────────────────────────────
echo ""
echo "═══ 1/5  Signing $APP_NAME.app ═══"
codesign --deep --force --verify --verbose \
    --sign "$SIGN_IDENTITY" \
    --options runtime \
    --entitlements "$ENTITLEMENTS" \
    "$APP_PATH"

# ── 2. Verify signature ────────────────────────────────────────────────────
echo ""
echo "═══ 2/5  Verifying signature ═══"
codesign --verify --verbose "$APP_PATH"

# ── 3. Zip for notarization ────────────────────────────────────────────────
echo ""
echo "═══ 3/5  Zipping for notarization ═══"
rm -f "$ZIP_PATH"
ditto -c -k --keepParent "$APP_PATH" "$ZIP_PATH"
echo "Created: $ZIP_PATH"

# ── 4. Notarize (blocks until done) ────────────────────────────────────────
echo ""
echo "═══ 4/5  Submitting to Apple for notarization ═══"
echo "This usually takes 1-10 minutes. Don't close this terminal."
xcrun notarytool submit "$ZIP_PATH" \
    --keychain-profile "$KEYCHAIN_PROFILE" \
    --wait

# ── 5. Staple + final zip ──────────────────────────────────────────────────
echo ""
echo "═══ 5/5  Stapling ticket and creating final zip ═══"
xcrun stapler staple "$APP_PATH"
xcrun stapler validate "$APP_PATH"
spctl --assess --type execute --verbose "$APP_PATH" || true

rm -f "$FINAL_ZIP"
ditto -c -k --keepParent "$APP_PATH" "$FINAL_ZIP"

# Clean up the intermediate zip
rm -f "$ZIP_PATH"

echo ""
echo "─────────────────────────────────────────────────────────────────────"
echo "✅ Done!"
echo "   Shippable zip: $FINAL_ZIP"
echo "─────────────────────────────────────────────────────────────────────"
