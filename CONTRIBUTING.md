# Contributing to BusDock Studio

Thank you for helping build a safer Service Bus explorer.

## Before you start

1. Search existing issues and open a focused issue for substantial changes.
2. Never include real connection strings, SAS keys, tokens, namespace names, or message bodies.
3. Keep the UI independent of Azure SDK models; map them in `BusDockStudio.AzureServiceBus`.
4. Treat destructive workflows as safety-critical. Do not introduce receive-and-delete fallbacks or selected-peek deletion.

## Development

Install .NET 10, then run:

```bash
dotnet restore BusDockStudio.sln
dotnet build BusDockStudio.sln --no-restore
dotnet test BusDockStudio.sln --no-build
```

Add unit tests using BusDock Studio interfaces and fakes. Default tests must not require a live Azure resource. Keep nullable annotations and analyzer warnings clean.

## Pull requests

Use a descriptive title, explain safety implications, list verification, and update documentation when behavior changes. Small, reviewable pull requests are preferred. By contributing, you agree that your work is licensed under MIT.
