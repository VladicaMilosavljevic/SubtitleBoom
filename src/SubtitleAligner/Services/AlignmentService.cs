using SubtitleAligner.Models;
using SubtitleAligner.Utilities;

namespace SubtitleAligner.Services;

public sealed class AlignmentService
{
    private const double GapCost = 0.85;
    private const double SubstituteCost = 1.0;
    private static readonly TimeSpan CueGap = TimeSpan.FromMilliseconds(80);

    private sealed record WordMatch(int TargetIndex, double Similarity);

    private sealed class CueMatch
    {
        public required int CueIndex { get; init; }
        public required int WordCount { get; init; }
        public required int[] TargetIndices { get; init; }
        public required double AverageSimilarity { get; init; }
        public bool IsAnchor { get; set; }
    }

    public List<ReviewSignal> Align(IReadOnlyList<SubtitleCue> cues, IReadOnlyList<RecognizedWord> recognized, string language = "auto")
    {
        RecognizedWord[] recognizedWords = recognized.Where(word => word.Normalized.Length > 0).ToArray();
        var sourceWords = new List<(int CueIndex, string Word)>();

        for (int cueIndex = 0; cueIndex < cues.Count; cueIndex++)
        {
            foreach (string word in TextNormalizer.GetWords(cues[cueIndex].Text))
                sourceWords.Add((cueIndex, NormalizeComparisonWord(word, language)));
        }

        if (sourceWords.Count == 0)
            throw new InvalidDataException("Tekst ne sadrži reči za poravnanje.");

        WordMatch[] mapping = BuildMapping(
            sourceWords.Select(x => x.Word).ToArray(),
            recognizedWords.Select(x => NormalizeComparisonWord(x.Normalized, language)).ToArray());

        List<CueMatch> cueMatches = BuildCueMatches(cues, sourceWords, mapping);
        MarkAnchors(cueMatches);

        // Najpre dodeljujemo vremena samo pouzdanim cue-ovima.
        foreach (CueMatch match in cueMatches.Where(x => x.IsAnchor))
            AssignFromRecognized(cues[match.CueIndex], match.TargetIndices, recognizedWords);

        // Zatim popunjavamo sve delove između sidara bez širenja greške dalje.
        FillBeforeFirstAnchor(cues, cueMatches, recognizedWords);
        FillBetweenAnchors(cues, cueMatches);
        FillAfterLastAnchor(cues, cueMatches, recognizedWords);

        NormalizeTimeline(cues);

        return BuildReviewSignals(
            cues,
            sourceWords,
            mapping,
            cueMatches,
            recognizedWords);
    }

    private static string NormalizeComparisonWord(string word, string language)
    {
        bool serbian = string.Equals(language, "sr", StringComparison.OrdinalIgnoreCase) ||
                       language.StartsWith("sr-", StringComparison.OrdinalIgnoreCase);
        return serbian ? TextNormalizer.NormalizeWord(SerbianScriptConverter.ToLatin(word)) : word;
    }


    private static List<ReviewSignal> BuildReviewSignals(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<(int CueIndex, string Word)> sourceWords,
        IReadOnlyList<WordMatch> mapping,
        IReadOnlyList<CueMatch> cueMatches,
        IReadOnlyList<RecognizedWord> recognized)
    {
        const int maxOpeningWords = 5;
        const double firstWordThreshold = 0.82;
        var result = new List<ReviewSignal>(cues.Count);

        for (int cueIndex = 0; cueIndex < cues.Count; cueIndex++)
        {
            CueMatch cueMatch = cueMatches[cueIndex];
            var cueWords = sourceWords
                .Select((item, index) => (item, index))
                .Where(x => x.item.CueIndex == cueIndex)
                .ToArray();
            var opening = cueWords.Take(maxOpeningWords).ToArray();

            int matched = 0;
            double similaritySum = 0.0;
            int orderedPairs = 0;
            double continuitySum = 0.0;
            int? firstConfirmedPosition = null;
            TimeSpan? firstConfirmedStart = null;
            double firstConfirmedConfidence = 0.0;
            int? previousSourcePosition = null;
            int? previousTargetIndex = null;

            for (int localIndex = 0; localIndex < opening.Length; localIndex++)
            {
                int sourceIndex = opening[localIndex].index;
                WordMatch wordMatch = mapping[sourceIndex];
                if (wordMatch.TargetIndex < 0 || wordMatch.TargetIndex >= recognized.Count)
                    continue;

                matched++;
                similaritySum += wordMatch.Similarity;

                if (!firstConfirmedPosition.HasValue && wordMatch.Similarity >= firstWordThreshold)
                {
                    firstConfirmedPosition = localIndex + 1;
                    firstConfirmedStart = recognized[wordMatch.TargetIndex].Start;
                    firstConfirmedConfidence = wordMatch.Similarity;
                }

                if (previousSourcePosition.HasValue && previousTargetIndex.HasValue)
                {
                    int sourceGap = (localIndex + 1) - previousSourcePosition.Value;
                    int targetGap = wordMatch.TargetIndex - previousTargetIndex.Value;
                    if (sourceGap > 0 && targetGap > 0)
                    {
                        int difference = Math.Abs(sourceGap - targetGap);
                        continuitySum += difference switch
                        {
                            0 => 1.00,
                            1 => 0.80,
                            2 => 0.55,
                            _ => 0.20
                        };
                        orderedPairs++;
                    }
                }
                previousSourcePosition = localIndex + 1;
                previousTargetIndex = wordMatch.TargetIndex;
            }

            double phraseCoverage = opening.Length == 0 ? 0.0 : matched / (double)opening.Length;
            double phraseSimilarity = matched == 0 ? 0.0 : similaritySum / matched;
            double phraseContinuity = orderedPairs == 0 ? (matched == 1 ? 0.50 : 0.0) : continuitySum / orderedPairs;
            double phraseConfidence = 0.45 * phraseSimilarity + 0.35 * phraseCoverage + 0.20 * phraseContinuity;
            int minimumMatches = opening.Length switch { <= 1 => 1, 2 => 2, _ => 3 };

            string status =
                matched >= minimumMatches && phraseCoverage >= 0.67 && phraseSimilarity >= 0.80 && phraseContinuity >= 0.70 && phraseConfidence >= 0.79
                    ? "STRONG"
                    : matched >= minimumMatches && phraseCoverage >= 0.55 && phraseSimilarity >= 0.70 && phraseContinuity >= 0.55 && phraseConfidence >= 0.68
                        ? "MODERATE"
                        : "WEAK";

            int[] validTargets = cueMatch.TargetIndices
                .Where(index => index >= 0 && index < recognized.Count)
                .OrderBy(index => index)
                .ToArray();
            TimeSpan? detectedSpeechStart = validTargets.Length == 0 ? null : recognized[validTargets[0]].Start;
            TimeSpan? detectedSpeechEnd = validTargets.Length == 0 ? null : recognized[validTargets[^1]].End;

            result.Add(new ReviewSignal
            {
                CueNumber = cueIndex + 1,
                WordCount = cueMatch.WordCount,
                MatchedWordCount = cueMatch.TargetIndices.Length,
                Coverage = (double)cueMatch.TargetIndices.Length / Math.Max(1, cueMatch.WordCount),
                AverageSimilarity = cueMatch.AverageSimilarity,
                IsAnchor = cueMatch.IsAnchor,
                FirstConfirmedWordPosition = firstConfirmedPosition,
                FirstConfirmedWordStart = firstConfirmedStart,
                DetectedSpeechStart = detectedSpeechStart,
                DetectedSpeechEnd = detectedSpeechEnd,
                FirstConfirmedWordConfidence = firstConfirmedConfidence,
                OpeningPhraseWordCount = opening.Length,
                OpeningPhraseMatchedWords = matched,
                OpeningPhraseCoverage = phraseCoverage,
                OpeningPhraseSimilarity = phraseSimilarity,
                OpeningPhraseContinuity = phraseContinuity,
                OpeningPhraseConfidence = phraseConfidence,
                OpeningPhraseStatus = status
            });
        }

        return result;
    }

    private static List<CueMatch> BuildCueMatches(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<(int CueIndex, string Word)> sourceWords,
        IReadOnlyList<WordMatch> mapping)
    {
        var result = new List<CueMatch>();

        for (int cueIndex = 0; cueIndex < cues.Count; cueIndex++)
        {
            int[] sourceIndices = sourceWords
                .Select((word, index) => (word, index))
                .Where(x => x.word.CueIndex == cueIndex)
                .Select(x => x.index)
                .ToArray();

            WordMatch[] matches = sourceIndices
                .Select(index => mapping[index])
                .Where(x => x.TargetIndex >= 0)
                .ToArray();

            int[] targetIndices = matches
                .Select(x => x.TargetIndex)
                .Distinct()
                .Order()
                .ToArray();

            double averageSimilarity = matches.Length == 0
                ? 0.0
                : matches.Average(x => x.Similarity);

            result.Add(new CueMatch
            {
                CueIndex = cueIndex,
                WordCount = Math.Max(1, sourceIndices.Length),
                TargetIndices = targetIndices,
                AverageSimilarity = averageSimilarity
            });
        }

        return result;
    }

    private static void MarkAnchors(IReadOnlyList<CueMatch> matches)
    {
        foreach (CueMatch match in matches)
        {
            int minimumMatches = match.WordCount <= 4 ? 2 : 3;
            double coverage = (double)match.TargetIndices.Length / match.WordCount;

            match.IsAnchor =
                match.TargetIndices.Length >= minimumMatches &&
                coverage >= 0.45 &&
                match.AverageSimilarity >= 0.62;
        }

        // Izolovano slabo sidro može biti lažni pogodak.
        for (int i = 0; i < matches.Count; i++)
        {
            if (!matches[i].IsAnchor)
                continue;

            bool hasNeighbour =
                (i > 0 && matches[i - 1].IsAnchor) ||
                (i + 1 < matches.Count && matches[i + 1].IsAnchor);

            if (!hasNeighbour &&
                matches[i].AverageSimilarity < 0.78 &&
                matches[i].TargetIndices.Length < 4)
            {
                matches[i].IsAnchor = false;
            }
        }
    }

    private static void AssignFromRecognized(
        SubtitleCue cue,
        IReadOnlyList<int> indices,
        IReadOnlyList<RecognizedWord> recognized)
    {
        TimeSpan start = recognized[indices[0]].Start - TimeSpan.FromMilliseconds(70);
        TimeSpan end = recognized[indices[^1]].End + TimeSpan.FromMilliseconds(120);

        if (start < TimeSpan.Zero)
            start = TimeSpan.Zero;

        TimeSpan minimumEnd = start + TimeSpan.FromMilliseconds(500);
        cue.Start = start;
        cue.End = end < minimumEnd ? minimumEnd : end;
    }

    private static void FillBeforeFirstAnchor(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<CueMatch> matches,
        IReadOnlyList<RecognizedWord> recognized)
    {
        int firstAnchor = matches.ToList().FindIndex(x => x.IsAnchor);

        if (firstAnchor < 0)
        {
            // Krajnja rezerva: nema nijednog pouzdanog sidra.
            TimeSpan availableEnd = recognized.Count > 0
                ? recognized[^1].End
                : TimeSpan.FromSeconds(Math.Max(1, cues.Count * 2));

            Distribute(cues, matches, 0, cues.Count - 1, TimeSpan.Zero, availableEnd);
            return;
        }

        if (firstAnchor == 0)
            return;

        TimeSpan end = cues[firstAnchor].Start - CueGap;
        if (end < TimeSpan.Zero)
            end = TimeSpan.Zero;

        Distribute(cues, matches, 0, firstAnchor - 1, TimeSpan.Zero, end);
    }

    private static void FillBetweenAnchors(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<CueMatch> matches)
    {
        int previousAnchor = -1;

        for (int i = 0; i < matches.Count; i++)
        {
            if (!matches[i].IsAnchor)
                continue;

            if (previousAnchor >= 0 && i - previousAnchor > 1)
            {
                TimeSpan start = cues[previousAnchor].End + CueGap;
                TimeSpan end = cues[i].Start - CueGap;

                Distribute(cues, matches, previousAnchor + 1, i - 1, start, end);
            }

            previousAnchor = i;
        }
    }

    private static void FillAfterLastAnchor(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<CueMatch> matches,
        IReadOnlyList<RecognizedWord> recognized)
    {
        int lastAnchor = -1;
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i].IsAnchor)
            {
                lastAnchor = i;
                break;
            }
        }

        if (lastAnchor < 0 || lastAnchor == cues.Count - 1)
            return;

        TimeSpan start = cues[lastAnchor].End + CueGap;
        TimeSpan recognizedEnd = recognized.Count > 0
            ? recognized[^1].End + TimeSpan.FromMilliseconds(250)
            : start + TimeSpan.FromSeconds((cues.Count - lastAnchor - 1) * 2);

        Distribute(cues, matches, lastAnchor + 1, cues.Count - 1, start, recognizedEnd);
    }

    private static void Distribute(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<CueMatch> matches,
        int first,
        int last,
        TimeSpan windowStart,
        TimeSpan windowEnd)
    {
        if (first > last)
            return;

        int count = last - first + 1;
        double minimumSeconds = count * 0.55 + Math.Max(0, count - 1) * CueGap.TotalSeconds;

        if (windowEnd <= windowStart ||
            (windowEnd - windowStart).TotalSeconds < minimumSeconds)
        {
            windowEnd = windowStart + TimeSpan.FromSeconds(minimumSeconds);
        }

        int totalWeight = 0;
        for (int i = first; i <= last; i++)
            totalWeight += Math.Max(1, matches[i].WordCount);

        double usableSeconds =
            (windowEnd - windowStart).TotalSeconds -
            Math.Max(0, count - 1) * CueGap.TotalSeconds;

        TimeSpan cursor = windowStart;

        for (int i = first; i <= last; i++)
        {
            double share = usableSeconds *
                Math.Max(1, matches[i].WordCount) /
                Math.Max(1, totalWeight);

            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0.55, share));
            cues[i].Start = cursor;
            cues[i].End = cursor + duration;
            cursor = cues[i].End + CueGap;
        }
    }

    private static void NormalizeTimeline(IReadOnlyList<SubtitleCue> cues)
    {
        TimeSpan previousEnd = TimeSpan.Zero;

        for (int i = 0; i < cues.Count; i++)
        {
            SubtitleCue cue = cues[i];

            TimeSpan minimumStart = i == 0
                ? TimeSpan.Zero
                : previousEnd + CueGap;

            if (cue.Start < minimumStart)
                cue.Start = minimumStart;

            TimeSpan minimumDuration = TimeSpan.FromMilliseconds(500);
            if (cue.End < cue.Start + minimumDuration)
                cue.End = cue.Start + minimumDuration;

            // Sprečava predugačke cue-ove nastale usled lošeg lokalnog pogotka.
            TimeSpan maximumDuration = TimeSpan.FromSeconds(
                Math.Clamp(cue.Text.Replace(Environment.NewLine, " ").Length / 12.0, 2.0, 7.0));

            if (cue.End - cue.Start > maximumDuration)
                cue.End = cue.Start + maximumDuration;

            previousEnd = cue.End;
        }
    }

    private static WordMatch[] BuildMapping(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target)
    {
        int n = source.Count;
        int m = target.Count;
        var dp = new double[n + 1, m + 1];
        var move = new byte[n + 1, m + 1];

        for (int i = 1; i <= n; i++)
        {
            dp[i, 0] = i * GapCost;
            move[i, 0] = 2;
        }

        for (int j = 1; j <= m; j++)
        {
            dp[0, j] = j * GapCost;
            move[0, j] = 3;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double similarity = Similarity(source[i - 1], target[j - 1]);
                double diagonal = dp[i - 1, j - 1] + (1.0 - similarity) * SubstituteCost;
                double up = dp[i - 1, j] + GapCost;
                double left = dp[i, j - 1] + GapCost;

                if (diagonal <= up && diagonal <= left)
                {
                    dp[i, j] = diagonal;
                    move[i, j] = 1;
                }
                else if (up <= left)
                {
                    dp[i, j] = up;
                    move[i, j] = 2;
                }
                else
                {
                    dp[i, j] = left;
                    move[i, j] = 3;
                }
            }
        }

        var result = Enumerable
            .Repeat(new WordMatch(-1, 0.0), n)
            .ToArray();

        int x = n;
        int y = m;

        while (x > 0 || y > 0)
        {
            byte current = move[x, y];

            if (current == 1)
            {
                double similarity = Similarity(source[x - 1], target[y - 1]);

                if (similarity >= 0.42)
                    result[x - 1] = new WordMatch(y - 1, similarity);

                x--;
                y--;
            }
            else if (current == 2)
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return result;
    }

    private static double Similarity(string a, string b)
    {
        if (a == b)
            return 1.0;

        if (a.Length == 0 || b.Length == 0)
            return 0.0;

        int distance = Levenshtein(a, b);
        return 1.0 - (double)distance / Math.Max(a.Length, b.Length);
    }

    private static int Levenshtein(string a, string b)
    {
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
        int[] current = new int[b.Length + 1];

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
