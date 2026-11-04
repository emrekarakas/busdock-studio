# macOS packaging

## Local Apple Silicon bundle

`scripts/package-macos.sh` performs a Release, `osx-arm64`, self-contained publish and assembles `artifacts/BusDockStudio.app`. It does not require or invent an Apple certificate.

```bash
./scripts/package-macos.sh
open artifacts/BusDockStudio.app
```

The executable name is `BusDockStudio`; Finder displays `BusDock Studio` from `Info.plist`.

## Code signing and notarization

For a public release, obtain a real Developer ID Application certificate and configure credentials outside the repository. Sign nested binaries first and the bundle last with hardened runtime. Verify with `codesign --verify --deep --strict --verbose=2` and `spctl --assess --type execute --verbose=2`.

Submit a ZIP or DMG with Apple's `notarytool`, wait for acceptance, then staple the ticket. Never commit certificate files, app-specific passwords, issuer IDs, API keys, or GitHub secrets. Release automation should reference administrator-provisioned secret names only after the project owner approves them.

## DMG

After signing and notarization are configured, package the `.app` into a read-only DMG, notarize that artifact, staple it, and verify on a clean macOS account. The MVP intentionally stops at an unsigned local `.app`.
