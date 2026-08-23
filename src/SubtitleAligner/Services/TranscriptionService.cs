using System.Text;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public sealed class TranscriptionService
{
    private const int MaxLineCharacters = 44;
    private const double MaxCueSeconds = 7.0;
    private const double PauseSplitSeconds = 0.85;

    public List<SubtitleCue> BuildCues(IReadOnlyList<RecognizedWord> tokens, string? serbianScriptMode = null)
    {
        List<TranscriptWord> words = BuildCompleteWords(tokens);
        if (serbianScriptMode == "Latinica")
            foreach (TranscriptWord word in words) word.Text = SerbianScriptConverter.ToLatin(word.Text);
        else if (serbianScriptMode == "Ćirilica")
            foreach (TranscriptWord word in words) word.Text = SerbianScriptConverter.ToCyrillic(word.Text);
        var cues = new List<SubtitleCue>();
        var current = new List<TranscriptWord>();

        foreach (TranscriptWord word in words)
        {
            string candidate = JoinWords(current.Append(word));
            bool split = current.Count > 0 &&
                ((word.Start - current[^1].End).TotalSeconds >= PauseSplitSeconds ||
                 (word.End - current[0].Start).TotalSeconds > MaxCueSeconds ||
                 !CanFitInTwoLines(candidate));

            if (split) AddCue(cues, current);
            current.Add(word);

            if (EndsSentence(word.Text) && JoinWords(current).Length >= 24)
                AddCue(cues, current);
            else if (EndsClause(word.Text) && JoinWords(current).Length >= 50)
                AddCue(cues, current);
        }
        AddCue(cues, current);
        PreventOverlaps(cues);
        return cues;
    }

    public List<ReviewSignal> BuildSignals(IReadOnlyList<SubtitleCue> cues, IReadOnlyList<RecognizedWord> tokens)
    {
        return cues.Select((cue, index) =>
        {
            RecognizedWord[] cueTokens = tokens
                .Where(w => w.Normalized.Length > 0 && w.Start < cue.End && w.End > cue.Start)
                .ToArray();
            int wordCount = BuildCompleteWords(cueTokens).Count;
            double confidence = cueTokens.Length == 0 ? 0 : cueTokens.Average(w => w.Probability);
            double cps = cue.Text.Replace("\r", "").Replace("\n", "").Length /
                         Math.Max(0.1, (cue.End - cue.Start).TotalSeconds);
            string status = confidence >= 0.72 && cps <= 20 ? "STRONG"
                : confidence >= 0.45 && cps <= 25 ? "MODERATE"
                : "WEAK";
            return new ReviewSignal
            {
                CueNumber = index + 1,
                WordCount = wordCount,
                MatchedWordCount = wordCount,
                Coverage = 1,
                AverageSimilarity = confidence,
                DetectedSpeechStart = cueTokens.FirstOrDefault()?.Start,
                DetectedSpeechEnd = cueTokens.LastOrDefault()?.End,
                OpeningPhraseConfidence = confidence,
                OpeningPhraseStatus = status
            };
        }).ToList();
    }

    public async Task SaveTextAsync(string plainPath, string? timedPath, IReadOnlyList<SubtitleCue> cues, CancellationToken token)
    {
        string plain = string.Join(" ", cues.Select(c => SingleLine(c.Text)));
        await File.WriteAllTextAsync(plainPath, plain + Environment.NewLine, Encoding.UTF8, token);
        if (string.IsNullOrWhiteSpace(timedPath)) return;

        var timed = new StringBuilder();
        foreach (SubtitleCue cue in cues)
        {
            timed.Append('[').Append(Format(cue.Start)).Append(" --> ").Append(Format(cue.End)).AppendLine("]");
            timed.AppendLine(SingleLine(cue.Text)).AppendLine();
        }
        await File.WriteAllTextAsync(timedPath, timed.ToString(), Encoding.UTF8, token);
    }

    private static List<TranscriptWord> BuildCompleteWords(IReadOnlyList<RecognizedWord> tokens)
    {
        var words = new List<TranscriptWord>();
        TranscriptWord? current = null;

        foreach (RecognizedWord token in tokens.OrderBy(t => t.Start).ThenBy(t => t.End))
        {
            string raw = token.Text;
            string value = raw.Trim();
            if (value.Length == 0) continue;

            bool punctuationOnly = token.Normalized.Length == 0 && value.All(c => char.IsPunctuation(c) || char.IsSymbol(c));
            bool beginsNewWord = !punctuationOnly && raw.Length > 0 && char.IsWhiteSpace(raw[0]);

            if (beginsNewWord && current is not null)
            {
                words.Add(current);
                current = null;
            }

            if (punctuationOnly)
            {
                if (current is not null)
                {
                    current.Text += value;
                    current.End = Later(current.End, token.End);
                }
                else if (words.Count > 0)
                {
                    words[^1].Text += value;
                    words[^1].End = Later(words[^1].End, token.End);
                }
                continue;
            }

            if (current is null)
            {
                current = new TranscriptWord(value, token.Start, token.End, token.Probability, 1);
            }
            else
            {
                current.Text += value;
                current.End = Later(current.End, token.End);
                current.ProbabilitySum += token.Probability;
                current.TokenCount++;
            }
        }

        if (current is not null) words.Add(current);
        return words;
    }

    private static void AddCue(List<SubtitleCue> cues, List<TranscriptWord> words)
    {
        if (words.Count == 0) return;
        string text = Wrap(JoinWords(words));
        TimeSpan start = words[0].Start;
        TimeSpan end = words[^1].End;
        if (end - start < TimeSpan.FromMilliseconds(700)) end = start + TimeSpan.FromMilliseconds(700);
        cues.Add(new SubtitleCue { Start = start, End = end, Text = text });
        words.Clear();
    }

    private static string JoinWords(IEnumerable<TranscriptWord> words)
        => string.Join(" ", words.Select(w => w.Text));

    private static bool CanFitInTwoLines(string text)
    {
        if (text.Length <= MaxLineCharacters) return true;
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] != ' ') continue;
            if (i <= MaxLineCharacters && text.Length - i - 1 <= MaxLineCharacters) return true;
        }
        return false;
    }

    private static string Wrap(string text)
    {
        if (text.Length <= MaxLineCharacters) return text;
        int best = -1;
        int bestScore = int.MaxValue;
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] != ' ') continue;
            int left = i;
            int right = text.Length - i - 1;
            if (left > MaxLineCharacters || right > MaxLineCharacters) continue;
            int score = Math.Abs(left - right);
            if (score < bestScore) { best = i; bestScore = score; }
        }
        return best < 0 ? text : text[..best] + Environment.NewLine + text[(best + 1)..];
    }

    private static void PreventOverlaps(List<SubtitleCue> cues)
    {
        TimeSpan minimumGap = TimeSpan.FromMilliseconds(80);
        for (int i = 0; i + 1 < cues.Count; i++)
        {
            if (cues[i + 1].Start - cues[i].End >= minimumGap) continue;
            TimeSpan end = cues[i + 1].Start - minimumGap;
            if (end > cues[i].Start) cues[i].End = end;
        }
    }

    private static bool EndsSentence(string text)
        => text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?');

    private static bool EndsClause(string text)
        => text.EndsWith(',') || text.EndsWith(';') || text.EndsWith(':');

    private static TimeSpan Later(TimeSpan first, TimeSpan second) => first >= second ? first : second;
    private static string SingleLine(string text) => text.Replace("\r", " ").Replace("\n", " ");
    private static string Format(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff");

    private sealed class TranscriptWord(string text, TimeSpan start, TimeSpan end, double probabilitySum, int tokenCount)
    {
        public string Text { get; set; } = text;
        public TimeSpan Start { get; } = start;
        public TimeSpan End { get; set; } = end;
        public double ProbabilitySum { get; set; } = probabilitySum;
        public int TokenCount { get; set; } = tokenCount;
    }
}
