using System.Text;
using System.Text.Json;
using BusDockStudio.Core.Models;

namespace BusDockStudio.Core.UseCases;

public static class MessageBodyFormatter
{
    private const int MaxDisplayCharacters = 1_000_000;
    private const int MaxBase64Bytes = 750_000;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static MessageBody Format(ReadOnlyMemory<byte> body)
    {
        var bytes = body.ToArray();
        var base64Length = Math.Min(bytes.Length, MaxBase64Bytes);
        var base64 = Convert.ToBase64String(bytes, 0, base64Length);
        if (base64Length < bytes.Length)
        {
            base64 += $"\n\n[Base64 display truncated at {base64Length:N0} of {bytes.Length:N0} bytes]";
        }

        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (TryFormatJson(text, out var json))
            {
                return new(bytes, MessageBodyKind.Json, Limit(json), base64);
            }

            return IsMostlyText(text)
                ? new(bytes, MessageBodyKind.Text, Limit(text), base64)
                : new(bytes, MessageBodyKind.Binary, base64, base64);
        }
        catch (DecoderFallbackException)
        {
            return new(bytes, MessageBodyKind.Binary, base64, base64);
        }
    }

    private static bool TryFormatJson(string value, out string formatted)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            formatted = JsonSerializer.Serialize(document.RootElement, IndentedJsonOptions);
            return true;
        }
        catch (JsonException)
        {
            formatted = string.Empty;
            return false;
        }
    }

    private static bool IsMostlyText(string value) =>
        value.Length == 0 || value.Count(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t') < Math.Max(1, value.Length / 20);

    private static string Limit(string value) => value.Length <= MaxDisplayCharacters
        ? value
        : $"{value[..MaxDisplayCharacters]}\n\n[Display truncated at {MaxDisplayCharacters:N0} characters]";
}
