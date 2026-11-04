using BusDockStudio.Core.Models;
using BusDockStudio.Core.UseCases;

namespace BusDockStudio.Tests;

public sealed class PeekPaginationTests
{
    [Fact]
    public void AdvancesFromLastSequenceNumber()
    {
        var messages = new[] { CreateMessage(41), CreateMessage(49) };

        Assert.Equal(50, PeekPagination.GetNextSequenceNumber(messages));
    }

    [Fact]
    public void EmptyPageHasNoNextCursor() =>
        Assert.Null(PeekPagination.GetNextSequenceNumber([]));

    private static MessageRecord CreateMessage(long sequenceNumber) => new(
        sequenceNumber, "id", null, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow.AddMinutes(1),
        0, "Active", null, null, null, null, null,
        new([], MessageBodyKind.Text, string.Empty, string.Empty),
        new Dictionary<string, string>(), new Dictionary<string, string>());
}
