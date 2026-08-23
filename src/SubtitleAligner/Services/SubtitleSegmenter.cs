using System.Text.RegularExpressions;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public sealed partial class SubtitleSegmenter
{
    private const int MaxLine = 42;
    private const int MaxCue = 84;
    private const int MaxWords = 14;

    public List<SubtitleCue> Segment(IReadOnlyList<SubtitleCue> input)
    {
        var output = new List<SubtitleCue>();

        foreach (var cue in input)
        {
            string text = WhiteSpaceRegex().Replace(cue.Text, " ").Trim();
            if (text.Length == 0)
                continue;

            foreach (string part in Split(text))
                output.Add(new SubtitleCue { Text = Wrap(part) });
        }

        return output;
    }

    private static IEnumerable<string> Split(string text)
    {
        foreach (string sentence in SentenceRegex().Split(text))
        {
            string clean = sentence.Trim();
            if (clean.Length == 0)
                continue;

            string[] words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new List<string>();

            foreach (string word in words)
            {
                string currentText = string.Join(" ", current);
                string candidate = string.Join(" ", current.Append(word));

                bool breakNow =
                    current.Count > 0 &&
                    (candidate.Length > MaxCue ||
                     current.Count >= MaxWords ||
                     (currentText.Length >= 60 && IsSoftBoundary(current[^1])));

                if (breakNow)
                {
                    yield return currentText;
                    current.Clear();
                }

                current.Add(word);
            }

            if (current.Count > 0)
                yield return string.Join(" ", current);
        }
    }

    private static string Wrap(string text)
    {
        if (text.Length <= MaxLine)
            return text;

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int best = -1;
        int bestScore = int.MaxValue;

        for (int i = 1; i < words.Length; i++)
        {
            string first = string.Join(" ", words[..i]);
            string second = string.Join(" ", words[i..]);

            if (first.Length > MaxLine || second.Length > MaxLine)
                continue;

            int score = Math.Abs(first.Length - second.Length);
            if (IsSoftBoundary(words[i - 1]))
                score -= 8;

            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best < 0)
            return text;

        return string.Join(" ", words[..best]) +
               Environment.NewLine +
               string.Join(" ", words[best..]);
    }

    private static bool IsSoftBoundary(string word) =>
        word.EndsWith(',') ||
        word.EndsWith(';') ||
        word.EndsWith(':') ||
        word.EndsWith('—') ||
        word.EndsWith('-');

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[\p{Lu}\p{N}])")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpaceRegex();
}
