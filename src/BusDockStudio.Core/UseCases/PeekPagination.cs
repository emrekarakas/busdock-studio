using BusDockStudio.Core.Models;

namespace BusDockStudio.Core.UseCases;

public static class PeekPagination
{
    public static long? GetNextSequenceNumber(IReadOnlyList<MessageRecord> messages) =>
        messages.Count == 0 ? null : checked(messages[^1].SequenceNumber + 1);
}
