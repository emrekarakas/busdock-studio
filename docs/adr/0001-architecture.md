# ADR 0001: Layered desktop architecture

- Status: Accepted
- Date: 2026-11-04

## Context

BusDock Studio must safely inspect and purge Azure Service Bus messages without coupling the desktop UI to Azure SDK types or persisting credentials in plain text.

## Decision

The solution has four production projects. `Core` owns immutable domain models, ports, formatting, pagination, confirmation, and purge orchestration. `AzureServiceBus` maps the Azure SDK to those ports. `Infrastructure` owns in-memory secret and settings implementations, redaction, and future platform adapters. `Desktop` is an Avalonia MVVM composition root and does not reference SDK types.

Connection strings are keyed by profile ID behind `ISecretStore`. The MVP store is memory-only; a future macOS Keychain implementation can replace it without changing the UI. Non-secret connection metadata is held behind `IApplicationSettings`.

Browsing uses peek receivers and sequence-number cursors. Purge captures exactly one UTC cutoff in the core coordinator, rejects partitioned entities, validates typed confirmation, prevents concurrent duplicate operations, and delegates only to the SDK's service-side purge API. There is no receive-and-delete fallback.

## Consequences

- Core behavior is testable without Azure.
- SDK upgrades are isolated to one adapter.
- Restarting the MVP intentionally forgets connection strings.
- Purge may leave locked, deferred, or scheduled messages and can be partially complete after interruption; the UI must state this explicitly.
- Topics are browse-only containers; message operations target queues or subscriptions.
