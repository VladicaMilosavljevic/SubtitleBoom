using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public static partial class SubtitleParser
{
    public const string OpenFilter = "Podržani titlovi i tekst|*.txt;*.srt;*.vtt;*.sbv;*.ass;*.ssa;*.ttml;*.dfxp|TXT tekst|*.txt|SubRip|*.srt|WebVTT|*.vtt|YouTube SBV|*.sbv|ASS / SSA|*.ass;*.ssa|TTML / DFXP|*.ttml;*.dfxp|Svi fajlovi|*.*";

    public static async Task<List<SubtitleCue>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".srt" => ParseSrt(text),
            ".vtt" => ParseVtt(text),
            ".sbv" => ParseSbv(text),
            ".ass" or ".ssa" => ParseAss(text),
            ".ttml" or ".dfxp" => ParseTtml(text),
            _ => ParseTxt(text)
        };
    }

    private static List<SubtitleCue> ParseTxt(string content) =>
        content.Replace("\r\n", "\n").Split('\n').Select(Clean).Where(line => line.Length > 0)
            .Select(line => new SubtitleCue { Text = line }).ToList();

    private static List<SubtitleCue> ParseSrt(string content) => ParseArrowBlocks(content, isVtt: false);

    private static List<SubtitleCue> ParseVtt(string content)
    {
        string normalized = Regex.Replace(content.Replace("\r\n", "\n"), @"^\s*WEBVTT[^\n]*\n", string.Empty, RegexOptions.IgnoreCase);
        return ParseArrowBlocks(normalized, isVtt: true);
    }

    private static List<SubtitleCue> ParseArrowBlocks(string content, bool isVtt)
    {
        var cues = new List<SubtitleCue>();
        string normalized = content.Replace("\r\n", "\n").Trim();
        foreach (string block in BlankLineRegex().Split(normalized))
        {
            string[] lines = block.Split('\n');
            int timingIndex = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0) continue;
            string[] parts = lines[timingIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            string endPart = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (!TryParseFlexibleTime(parts[0], out TimeSpan start) || !TryParseFlexibleTime(endPart, out TimeSpan end)) continue;
            string cueText = string.Join(" ", lines.Skip(timingIndex + 1).Select(Clean).Where(x => x.Length > 0));
            if (cueText.Length > 0) cues.Add(new SubtitleCue { Text = cueText, Start = start, End = end });
        }
        return cues;
    }

    private static List<SubtitleCue> ParseSbv(string content)
    {
        var cues = new List<SubtitleCue>();
        foreach (string block in BlankLineRegex().Split(content.Replace("\r\n", "\n").Trim()))
        {
            string[] lines = block.Split('\n');
            if (lines.Length < 2) continue;
            string[] times = lines[0].Split(',', StringSplitOptions.TrimEntries);
            if (times.Length != 2 || !TryParseFlexibleTime(times[0], out TimeSpan start) || !TryParseFlexibleTime(times[1], out TimeSpan end)) continue;
            string text = string.Join(" ", lines.Skip(1).Select(Clean).Where(x => x.Length > 0));
            if (text.Length > 0) cues.Add(new SubtitleCue { Start = start, End = end, Text = text });
        }
        return cues;
    }

    private static List<SubtitleCue> ParseAss(string content)
    {
        var cues = new List<SubtitleCue>();
        foreach (string raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            if (!raw.TrimStart().StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
            string payload = raw[(raw.IndexOf(':') + 1)..].Trim();
            string[] fields = payload.Split(',', 10);
            if (fields.Length < 10 || !TryParseFlexibleTime(fields[1], out TimeSpan start) || !TryParseFlexibleTime(fields[2], out TimeSpan end)) continue;
            string text = Clean(fields[9]);
            if (text.Length > 0) cues.Add(new SubtitleCue { Start = start, End = end, Text = text });
        }
        return cues;
    }

    private static List<SubtitleCue> ParseTtml(string content)
    {
        var cues = new List<SubtitleCue>();
        try
        {
            XDocument document = XDocument.Parse(content);
            foreach (XElement p in document.Descendants().Where(x => x.Name.LocalName == "p"))
            {
                string? begin = p.Attributes().FirstOrDefault(x => x.Name.LocalName == "begin")?.Value;
                string? endValue = p.Attributes().FirstOrDefault(x => x.Name.LocalName == "end")?.Value;
                string? dur = p.Attributes().FirstOrDefault(x => x.Name.LocalName == "dur")?.Value;
                if (begin is null || !TryParseFlexibleTime(begin, out TimeSpan start)) continue;
                TimeSpan end;
                if (endValue is not null && TryParseFlexibleTime(endValue, out TimeSpan parsedEnd)) end = parsedEnd;
                else if (dur is not null && TryParseFlexibleTime(dur, out TimeSpan parsedDur)) end = start + parsedDur;
                else continue;
                string text = Clean(string.Concat(p.DescendantNodes().Select(n => n is XText t ? t.Value : n is XElement e && e.Name.LocalName == "br" ? " " : string.Empty)));
                if (text.Length > 0) cues.Add(new SubtitleCue { Start = start, End = end, Text = text });
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            throw new InvalidDataException("TTML/DFXP fajl nije ispravan ili koristi nepodržanu strukturu.", ex);
        }
        return cues;
    }

    private static bool TryParseFlexibleTime(string value, out TimeSpan result)
    {
        string normalized = value.Trim().TrimEnd(',').Replace(',', '.');
        string[] formats = { @"hh\:mm\:ss\.fff", @"h\:mm\:ss\.fff", @"hh\:mm\:ss\.ff", @"h\:mm\:ss\.ff", @"hh\:mm\:ss", @"h\:mm\:ss" };
        if (TimeSpan.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, out result)) return true;
        if (normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase) && double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        { result = TimeSpan.FromSeconds(seconds); return true; }
        return TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out result);
    }

    private static string Clean(string value)
    {
        string result = HtmlRegex().Replace(value, " ");
        result = AssRegex().Replace(result, " ");
        result = result.Replace(@"\N", " ").Replace(@"\n", " ");
        return WhiteSpaceRegex().Replace(result, " ").Trim();
    }

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLineRegex();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlRegex();
    [GeneratedRegex(@"\{\\[^}]+\}")]
    private static partial Regex AssRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpaceRegex();
}
