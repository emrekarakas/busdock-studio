using System.Text.RegularExpressions;

namespace BusDockStudio.Infrastructure;

public static partial class SecretRedactor
{
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ConnectionSecretRegex().Replace(value, match => $"{match.Groups[1].Value}=***");
    }

    [GeneratedRegex("(?i)(SharedAccessKey|SharedAccessSignature|Password|Token)=([^;\\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionSecretRegex();
}
