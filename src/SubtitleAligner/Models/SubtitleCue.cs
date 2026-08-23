namespace SubtitleAligner.Models;

public sealed class SubtitleCue
{
    public required string Text { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}
