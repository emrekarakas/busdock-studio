# ADR 0001: Layered desktop architecture

- Status: Accepted
- Date: 2026-11-04

## Context

BusDock Studio must safely inspect and purge Azure Service Bus messages without coupling the desktop UI to Azure SDK types or persisting credentials in plain text.

## Decision

The solution has four production projects. `Core` owns immutable domain models, ports, formatting, pagination, confirmation, and purge orchestration. `AzureServiceBus` maps the Azure SDK to those ports. `Infrastructure` owns Keychain and file-backed persistence, in-memory test implementations, and redaction. `Desktop` is an Avalonia MVVM composition root and does not reference SDK types.

Connection strings are keyed by profile ID behind `ISecretStore` and stored as generic-password items in macOS Keychain. Non-secret connection metadata and the appearance preference are stored atomically behind `IApplicationSettings` in the user's Application Support directory. Other platforms retain the in-memory fallbacks until a native credential store is selected.

Browsing uses peek receivers and sequence-number cursors. Purge captures exactly one UTC cutoff in the core coordinator, rejects partitioned entities, requires explicit UI acknowledgement while retaining an internal target-name guard, prevents concurrent duplicate operations, and delegates only to the SDK's service-side purge API. There is no receive-and-delete fallback.

## Consequences

- Core behavior is testable without Azure.
- SDK upgrades are isolated to one adapter.
- Saved macOS connections are restored on launch; removing a profile also removes its Keychain item.
- Purge may leave locked, deferred, or scheduled messages and can be partially complete after interruption; the UI must state this explicitly.
- Topics are browse-only containers; message operations target queues or subscriptions.
