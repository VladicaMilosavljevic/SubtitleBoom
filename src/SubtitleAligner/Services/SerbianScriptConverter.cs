using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleAligner.Services;

public static partial class SerbianScriptConverter
{
    private static readonly Dictionary<char, string> LatinToCyrillic = new()
    {
        ['a']="а", ['b']="б", ['c']="ц", ['č']="ч", ['ć']="ћ", ['d']="д", ['đ']="ђ",
        ['e']="е", ['f']="ф", ['g']="г", ['h']="х", ['i']="и", ['j']="ј", ['k']="к",
        ['l']="л", ['m']="м", ['n']="н", ['o']="о", ['p']="п", ['r']="р", ['s']="с",
        ['š']="ш", ['t']="т", ['u']="у", ['v']="в", ['z']="з", ['ž']="ж"
    };

    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а']="a", ['б']="b", ['в']="v", ['г']="g", ['д']="d", ['ђ']="đ", ['е']="e",
        ['ж']="ž", ['з']="z", ['и']="i", ['ј']="j", ['к']="k", ['л']="l", ['љ']="lj",
        ['м']="m", ['н']="n", ['њ']="nj", ['о']="o", ['п']="p", ['р']="r", ['с']="s",
        ['т']="t", ['ћ']="ć", ['у']="u", ['ф']="f", ['х']="h", ['ц']="c", ['ч']="č",
        ['џ']="dž", ['ш']="š"
    };

    public static string ToCyrillic(string text) => WordRegex().Replace(text, match =>
    {
        string word = match.Value;
        if (ShouldPreserveLatin(word)) return word;
        var result = new StringBuilder();
        for (int i = 0; i < word.Length; i++)
        {
            if (i + 1 < word.Length)
            {
                string pair = word.Substring(i, 2).ToLowerInvariant();
                if (pair is "lj" or "nj" or "dž")
                {
                    string value = pair == "lj" ? "љ" : pair == "nj" ? "њ" : "џ";
                    result.Append(char.IsUpper(word[i]) ? value.ToUpperInvariant() : value);
                    i++;
                    continue;
                }
            }
            char lower = char.ToLowerInvariant(word[i]);
            if (!LatinToCyrillic.TryGetValue(lower, out string? mapped)) { result.Append(word[i]); continue; }
            result.Append(char.IsUpper(word[i]) ? mapped.ToUpperInvariant() : mapped);
        }
        return result.ToString();
    });

    public static string ToLatin(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            char lower = char.ToLowerInvariant(c);
            if (!CyrillicToLatin.TryGetValue(lower, out string? mapped)) { result.Append(c); continue; }
            result.Append(char.IsUpper(c) ? UppercaseLatin(mapped) : mapped);
        }
        return result.ToString();
    }

    private static bool ShouldPreserveLatin(string word)
    {
        if (word.Any(char.IsDigit)) return true;
        if (word.Length >= 2 && word.All(c => !char.IsLetter(c) || char.IsUpper(c))) return true;
        if (word.Skip(1).Any(char.IsUpper)) return true;
        return false;
    }

    private static string UppercaseLatin(string value)
        => value.Length == 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];

    [GeneratedRegex(@"[A-Za-zČĆŠŽĐčćšžđ]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
