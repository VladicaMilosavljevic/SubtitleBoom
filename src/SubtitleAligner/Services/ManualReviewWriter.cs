using System.Text;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public static class ManualReviewWriter
{
    private sealed record Item(
        SubtitleCue Cue,
        ReviewSignal Signal,
        int Score,
        string Level,
        string Reasons,
        double GapBefore,
        bool ContinuesPrevious,
        double? FirstWordDifference);

    public static async Task SaveAsync(
        string path,
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<ReviewSignal> signals,
        CancellationToken cancellationToken)
    {
        var items = new List<Item>();

        for (int i = 0; i < cues.Count; i++)
        {
            SubtitleCue cue = cues[i];
            ReviewSignal signal = signals[i];
            double gapBefore = i == 0 ? cue.Start.TotalSeconds : (cue.Start - cues[i - 1].End).TotalSeconds;
            bool continues = i > 0 && LooksLikeContinuation(cues[i - 1].Text, cue.Text);
            int score = 0;
            var reasons = new List<string>();

            if (signal.OpeningPhraseStatus == "WEAK")
            {
                score += 22;
                reasons.Add($"Opening Phrase WEAK ({signal.OpeningPhraseConfidence:0.00})");
            }
            else if (signal.OpeningPhraseStatus == "MODERATE")
            {
                score += 6;
                reasons.Add($"Opening Phrase MODERATE ({signal.OpeningPhraseConfidence:0.00})");
            }

            double? firstWordDifference = null;
            if (signal.FirstConfirmedWordStart.HasValue)
            {
                firstWordDifference = (signal.FirstConfirmedWordStart.Value - cue.Start).TotalSeconds;
                double difference = Math.Abs(firstWordDifference.Value);
                // Tiny model je namerno grublji od Base modela. Razlike ispod 0,50 s
                // uglavnom su kozmetiÄke i ne treba da pune Review laÅ¾nim alarmima.
                if (difference >= 1.50)
                {
                    score += 42;
                    reasons.Add($"veliko odstupanje prve reÄi {firstWordDifference.Value:+0.000;-0.000}s");
                }
                else if (difference >= 0.90)
                {
                    score += 28;
                    reasons.Add($"primetno odstupanje prve reÄi {firstWordDifference.Value:+0.000;-0.000}s");
                }
                else if (difference >= 0.50)
                {
                    score += 12;
                    reasons.Add($"moguÄ‡e odstupanje prve reÄi {firstWordDifference.Value:+0.000;-0.000}s");
                }
            }
            else
            {
                score += 10;
                reasons.Add("nijedna od prvih pet reÄi nije pouzdano potvrÄ‘ena");
            }

            if (signal.Coverage < 0.25) { score += 22; reasons.Add($"veoma nizak coverage ({signal.Coverage:P0})"); }
            else if (signal.Coverage < 0.45) { score += 8; reasons.Add($"nizak coverage ({signal.Coverage:P0})"); }

            if (signal.AverageSimilarity < 0.50) { score += 18; reasons.Add($"veoma niska sliÄnost ({signal.AverageSimilarity:0.00})"); }
            else if (signal.AverageSimilarity < 0.68) { score += 7; reasons.Add($"niska sliÄnost ({signal.AverageSimilarity:0.00})"); }

            if (!signal.IsAnchor) { score += 3; reasons.Add("cue nije stabilno vremensko sidro"); }

            if (gapBefore >= 3.00) { score += 8; reasons.Add($"veliki razmak pre cue-a ({gapBefore:0.000}s)"); }
            else if (gapBefore >= 2.00) { score += 3; reasons.Add($"primetan razmak pre cue-a ({gapBefore:0.000}s)"); }

            if (continues)
            {
                score += gapBefore >= 2.00 ? 18 : 6;
                reasons.Add("tekst deluje kao nastavak prethodne reÄenice");
            }
            if (continues && gapBefore >= 3.00)
            {
                score += 14;
                reasons.Add("veliki gap prekida istu reÄenicu");
            }

            if (score < 28)
                continue;

            string level = score >= 78 ? "VISOKA SUMNJA" : score >= 52 ? "SREDNJA SUMNJA" : "PROVERITI";
            items.Add(new Item(cue, signal, Math.Min(score, 100), level, string.Join("; ", reasons), gapBefore, continues, firstWordDifference));
        }

        items = items.OrderBy(x => x.Cue.Start).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("SUBTITLEBOOM v1.0 REVIEW");
        sb.AppendLine("MESTA ZA RUÄŒNU PROVERU");
        sb.AppendLine("====================================");
        sb.AppendLine();
        sb.AppendLine("SRT je napravljen netaknutim v0.5 Anti-Drift algoritmom.");
        sb.AppendLine("Review koristi tolerantnije pragove prilagoÄ‘ene brzom Tiny modelu za poravnanje.");
        sb.AppendLine();
        sb.AppendLine($"Ukupno oznaÄenih mesta: {items.Count}");
        sb.AppendLine($"Visoka sumnja: {items.Count(x => x.Level == "VISOKA SUMNJA")}");
        sb.AppendLine($"Srednja sumnja: {items.Count(x => x.Level == "SREDNJA SUMNJA")}");
        sb.AppendLine($"Proveriti: {items.Count(x => x.Level == "PROVERITI")}");
        sb.AppendLine();

        foreach (Item item in items)
        {
            sb.AppendLine(new string('-', 76));
            sb.AppendLine($"{item.Level} | Score {item.Score}/100 | Cue {item.Signal.CueNumber} | {Clock(item.Cue.Start)}");
            sb.AppendLine(new string('-', 76));
            sb.AppendLine($"Titl : {SrtTime(item.Cue.Start)}");
            sb.AppendLine(item.Signal.FirstConfirmedWordStart.HasValue
                ? $"Govor: {SrtTime(item.Signal.FirstConfirmedWordStart.Value)}"
                : "Govor: NIJE PRONAÄEN");
            sb.AppendLine($"Vreme titla: {SrtTime(item.Cue.Start)} --> {SrtTime(item.Cue.End)}");
            sb.AppendLine($"Tekst: {Flat(item.Cue.Text)}");
            sb.AppendLine($"Razlozi: {item.Reasons}");
            sb.AppendLine($"Opening Phrase: {item.Signal.OpeningPhraseStatus} | confidence {item.Signal.OpeningPhraseConfidence:0.00}");
            if (item.Signal.FirstConfirmedWordStart.HasValue)
            {
                if (item.FirstWordDifference.HasValue)
                {
                    string direction = item.FirstWordDifference.Value > 0 ? "kasnije" : "ranije";
                    sb.AppendLine($"Razlika: {Math.Abs(item.FirstWordDifference.Value):0.000}s {direction} od poÄetka cue-a");
                }
            }
            if (item.GapBefore > 0.0)
                sb.AppendLine($"Razmak od prethodnog cue-a: {item.GapBefore:0.000}s");
            sb.AppendLine(item.Signal.FirstConfirmedWordStart.HasValue
                ? $"Akcija: titl pronaÄ‘i na {Clock(item.Cue.Start)}, a govor na {Clock(item.Signal.FirstConfirmedWordStart.Value)}."
                : $"Akcija: titl pronaÄ‘i na {Clock(item.Cue.Start)}; odgovarajuÄ‡i govor nije pouzdano pronaÄ‘en.");
            sb.AppendLine();
        }

        sb.AppendLine("NAPOMENA: Notes je pomoÄ‡, ne konaÄna presuda. MoguÄ‡i su laÅ¾ni alarmi i veoma suptilne greÅ¡ke mogu ostati neoznaÄene.");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    private static bool LooksLikeContinuation(string previous, string current)
    {
        string prev = Flat(previous);
        string curr = Flat(current);
        if (string.IsNullOrWhiteSpace(prev) || string.IsNullOrWhiteSpace(curr)) return false;
        char last = prev[^1];
        bool strongEnd = last is '.' or '!' or '?' or '\u2026' or ':' or ';' or '"' or '\u201D';
        string firstToken = curr.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        firstToken = firstToken.Trim('"','\u201C','\u201D','\'','\u2018','\u2019','-','\u2013','\u2014','(','[','{');
        bool startsLower = firstToken.Length > 0 && char.IsLetter(firstToken[0]) && char.IsLower(firstToken[0]);
        bool continuationWord = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "and","or","but","because","that","which","who","where","when","while","so","than",
            "i","a","ali","pa","jer","da","koji","koja","koje","Å¡to","dok","onda","nego"
        }.Contains(firstToken);
        return !strongEnd && (startsLower || continuationWord);
    }

    private static string SrtTime(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    private static string Flat(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
}

