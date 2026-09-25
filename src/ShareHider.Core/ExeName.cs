namespace ShareHider.Core;

/// <summary>Validation for the exe file names users put on the hide list.</summary>
public static class ExeName
{
    public const int MaxLength = 260;

    // Windows' invalid file-name characters. Spelled out rather than taken from
    // Path.GetInvalidFileNameChars(), which returns a much shorter list on Linux.
    private static readonly char[] Invalid = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Normalizes a user-entered exe name to lower case ("Chrome.EXE " → "chrome.exe").
    /// Rejects anything with a path, wildcard or control character.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (input is null)
        {
            return false;
        }

        var name = input.Trim().ToLowerInvariant();
        if (name.Length is <= 4 or > MaxLength
            || !name.EndsWith(".exe", StringComparison.Ordinal)
            || name.IndexOfAny(Invalid) >= 0
            || name.Any(char.IsControl))
        {
            return false;
        }

        normalized = name;
        return true;
    }
}
