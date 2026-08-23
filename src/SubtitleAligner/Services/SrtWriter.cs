using System.Text;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public static class SrtWriter
{
    public static async Task SaveAsync(
        string path,
        IReadOnlyList<SubtitleCue> cues,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        for (int i = 0; i < cues.Count; i++)
        {
            SubtitleCue cue = cues[i];
            builder.AppendLine((i + 1).ToString());
            builder.Append(Format(cue.Start))
                .Append(" --> ")
                .AppendLine(Format(cue.End));
            builder.AppendLine(cue.Text);
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    private static string Format(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";
}
