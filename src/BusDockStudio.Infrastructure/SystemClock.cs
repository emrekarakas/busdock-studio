using BusDockStudio.Core.Services;

namespace BusDockStudio.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
