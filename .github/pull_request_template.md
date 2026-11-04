## Summary

Describe the user-visible change and why it is needed.

## Safety and privacy

- [ ] No credential, token, namespace, SAS key, or message body is committed or logged.
- [ ] Destructive behavior remains explicit, cutoff-bound, cancellable, and fail-closed.
- [ ] The UI does not directly depend on Azure SDK models.

## Verification

- [ ] `dotnet build BusDockStudio.sln`
- [ ] `dotnet test BusDockStudio.sln --no-build`
- [ ] Relevant macOS UI path checked manually

## Documentation

List updated documentation, or explain why none is needed.
