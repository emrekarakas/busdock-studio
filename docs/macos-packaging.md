# macOS packaging

## Local DMG

`scripts/package-macos.sh` performs a self-contained Release publish, assembles a macOS application bundle, applies an ad-hoc local signature, and creates a verified DMG with an Applications shortcut. The default target is Apple Silicon.

```bash
./scripts/package-macos.sh
open artifacts/BusDockStudio-0.1.0-macos-arm64.dmg
```

Override the version or build number without changing tracked files:

```bash
VERSION=0.2.0 BUILD_NUMBER=2 ./scripts/package-macos.sh osx-arm64
```

The executable is `BusDockStudio`; Finder displays `BusDock Studio` from `Info.plist`. The package is self-contained and does not require a separate .NET installation.

## Code signing and notarization

The generated DMG is intended for local testing. For public distribution, obtain a Developer ID Application certificate and configure credentials outside the repository. Sign nested binaries first and the bundle last with hardened runtime. Verify with `codesign --verify --deep --strict --verbose=2` and `spctl --assess --type execute --verbose=2`.

Submit a ZIP or DMG with Apple's `notarytool`, wait for acceptance, then staple the ticket. Never commit certificate files, app-specific passwords, issuer IDs, API keys, or GitHub secrets. Release automation should reference administrator-provisioned secret names only after the project owner approves them.

After Developer ID signing is configured, notarize the DMG with Apple's `notarytool`, staple the ticket, and verify on a clean macOS account before publishing a GitHub Release.
