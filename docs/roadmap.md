# Roadmap

## Next: credential and authentication foundations

- Implement a macOS Keychain-backed `ISecretStore` with migration from memory-only sessions.
- Add `InteractiveBrowserCredential` and least-privilege role guidance without changing Core contracts.
- Persist only non-secret connection metadata and theme preferences.

## DLQ resend design

DLQ resend is deliberately not part of the MVP. The planned workflow will:

- send a queue DLQ message back to its source queue;
- send a subscription DLQ message to the parent topic;
- create and send the replacement before completing the old DLQ message;
- clearly warn that send success followed by complete failure can create a duplicate;
- investigate same-namespace Service Bus transactions before choosing the final settlement design;
- avoid blindly copying dead-letter reason, error description, lock, sequence, delivery, and other broker-owned system fields;
- preserve appropriate user application properties and expose an explicit preview before sending;
- use idempotency guidance and an operation receipt where possible.

## Packaging and platform

- Replace the screenshot placeholder and add an original application icon.
- Add signed and notarized `.app` and DMG release automation when an Apple Developer certificate is available.
- Add `osx-x64` testing and packaging for Intel macOS.
- Evaluate Windows and Linux packaging after the macOS workflow is stable.

## Product work

- Saved filters, richer property queries, session browsing, export with redaction, and configurable large-body limits.
- Keychain health diagnostics and credential rotation.
- Accessibility audit, localization, and expanded keyboard shortcuts.
- Replace the purge compatibility bridge when the Azure SDK publishes the operation as a stable public API.
