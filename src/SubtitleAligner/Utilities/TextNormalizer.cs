using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleAligner.Utilities;

public static partial class TextNormalizer
{
    public static string NormalizeWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark && (char.IsLetterOrDigit(c) || c is '\'' or '’'))
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> GetWords(string text) =>
        WordRegex().Matches(text)
            .Select(m => NormalizeWord(m.Value))
            .Where(w => w.Length > 0)
            .ToArray();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
