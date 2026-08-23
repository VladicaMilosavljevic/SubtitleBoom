using System.Globalization;
using System.Text;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public static class SubtitleFormatWriter
{
    public const string SaveFilter = "SubRip (*.srt)|*.srt|WebVTT (*.vtt)|*.vtt|YouTube SBV (*.sbv)|*.sbv|Advanced SubStation Alpha (*.ass)|*.ass|Običan tekst (*.txt)|*.txt";

    public static Task SaveAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken token)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".vtt" => WriteVttAsync(path, cues, token),
            ".sbv" => WriteSbvAsync(path, cues, token),
            ".ass" or ".ssa" => WriteAssAsync(path, cues, token),
            ".txt" => WriteTxtAsync(path, cues, token),
            _ => SrtWriter.SaveAsync(path, cues, token)
        };
    }

    public static async Task ExportYouTubeSetAsync(string basePath, IReadOnlyList<SubtitleCue> cues, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(basePath) ?? AppContext.BaseDirectory;
        string name = Path.GetFileNameWithoutExtension(basePath);
        await SrtWriter.SaveAsync(Path.Combine(directory, name + ".srt"), cues, token);
        await WriteVttAsync(Path.Combine(directory, name + ".vtt"), cues, token);
        await WriteSbvAsync(Path.Combine(directory, name + ".sbv"), cues, token);
    }

    private static async Task WriteVttAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken token)
    {
        var sb = new StringBuilder("WEBVTT\r\n\r\n");
        foreach (SubtitleCue cue in cues)
        {
            sb.Append(FormatVtt(cue.Start)).Append(" --> ").Append(FormatVtt(cue.End)).Append("\r\n");
            sb.Append(cue.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), token);
    }

    private static async Task WriteSbvAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken token)
    {
        var sb = new StringBuilder();
        foreach (SubtitleCue cue in cues)
        {
            sb.Append(FormatSbv(cue.Start)).Append(',').Append(FormatSbv(cue.End)).Append("\r\n");
            sb.Append(cue.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), token);
    }

    private static async Task WriteAssAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken token)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine("Style: Default,Arial,44,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,30,30,30,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (SubtitleCue cue in cues)
        {
            string text = cue.Text.Replace("\r\n", "\\N").Replace("\n", "\\N").Replace(",", "，");
            sb.Append("Dialogue: 0,").Append(FormatAss(cue.Start)).Append(',').Append(FormatAss(cue.End))
              .Append(",Default,,0,0,0,,").AppendLine(text);
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true), token);
    }

    private static Task WriteTxtAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken token) =>
        File.WriteAllTextAsync(path, string.Join(Environment.NewLine, cues.Select(c => c.Text)), new UTF8Encoding(false), token);

    private static string FormatVtt(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    private static string FormatSbv(TimeSpan value) => $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    private static string FormatAss(TimeSpan value) => $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}";
}
