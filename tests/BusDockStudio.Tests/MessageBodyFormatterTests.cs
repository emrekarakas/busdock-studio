using System.Text;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.UseCases;

namespace BusDockStudio.Tests;

public sealed class MessageBodyFormatterTests
{
    [Fact]
    public void PrettyPrintsJson()
    {
        var result = MessageBodyFormatter.Format(Encoding.UTF8.GetBytes("{\"ok\":true}"));

        Assert.Equal(MessageBodyKind.Json, result.Kind);
        Assert.Contains(Environment.NewLine, result.DisplayText, StringComparison.Ordinal);
        Assert.Contains("\"ok\": true", result.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidJsonFallsBackToText()
    {
        var result = MessageBodyFormatter.Format(Encoding.UTF8.GetBytes("{not-json"));

        Assert.Equal(MessageBodyKind.Text, result.Kind);
        Assert.Equal("{not-json", result.DisplayText);
    }

    [Fact]
    public void BinaryBodyUsesBase64()
    {
        var result = MessageBodyFormatter.Format(new byte[] { 0xff, 0x00, 0x10 });

        Assert.Equal(MessageBodyKind.Binary, result.Kind);
        Assert.Equal(Convert.ToBase64String(new byte[] { 0xff, 0x00, 0x10 }), result.DisplayText);
    }
}
