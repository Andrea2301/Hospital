using System.Text.RegularExpressions;

namespace HospitalSV.Common;

/// <summary>
/// Security utility helpers for sanitizing and validating user inputs,
/// preventing null-byte attacks, script injection, and trimming malicious formatting.
/// </summary>
public static class SanitizationHelper
{
    private static readonly Regex ControlCharsRegex = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Strips control characters, null bytes, trims leading/trailing whitespace,
    /// and replaces multiple consecutive spaces with a single space.
    /// </summary>
    public static string Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove dangerous control chars / null bytes
        var cleaned = ControlCharsRegex.Replace(input, string.Empty);
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();
        return cleaned;
    }

    /// <summary>
    /// Normalizes and trims emails to lowercase.
    /// </summary>
    public static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        return Clean(email).ToLowerInvariant();
    }
}
