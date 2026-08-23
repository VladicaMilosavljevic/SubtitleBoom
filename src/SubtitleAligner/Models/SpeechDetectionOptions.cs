namespace SubtitleAligner.Models;

public sealed record SpeechDetectionOptions(
    string FfmpegPath,
    string WhisperPath,
    string ModelPath,
    string Language,
    string? AlignmentLanguage = null);
