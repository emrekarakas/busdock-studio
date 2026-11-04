namespace BusDockStudio.Core.Models;

public sealed record ConnectionProfile(Guid Id, string DisplayName, string FullyQualifiedNamespace);

public sealed record ConnectionTestResult(bool IsSuccess, string? FullyQualifiedNamespace, string Message);
