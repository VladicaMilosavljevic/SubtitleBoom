namespace SubtitleAligner.Models;

public sealed class ReviewSignal
{
    public int CueNumber { get; init; }
    public int WordCount { get; init; }
    public int MatchedWordCount { get; init; }
    public double Coverage { get; init; }
    public double AverageSimilarity { get; init; }
    public bool IsAnchor { get; init; }
    public int? FirstConfirmedWordPosition { get; init; }
    public TimeSpan? FirstConfirmedWordStart { get; init; }
    public TimeSpan? DetectedSpeechStart { get; init; }
    public TimeSpan? DetectedSpeechEnd { get; init; }
    public double FirstConfirmedWordConfidence { get; init; }
    public int OpeningPhraseWordCount { get; init; }
    public int OpeningPhraseMatchedWords { get; init; }
    public double OpeningPhraseCoverage { get; init; }
    public double OpeningPhraseSimilarity { get; init; }
    public double OpeningPhraseContinuity { get; init; }
    public double OpeningPhraseConfidence { get; init; }
    public required string OpeningPhraseStatus { get; init; }
}
