namespace SubtitleAligner.Models;

public sealed record RecognizedWord(
    string Text,
    string Normalized,
    TimeSpan Start,
    TimeSpan End,
    double Probability);
