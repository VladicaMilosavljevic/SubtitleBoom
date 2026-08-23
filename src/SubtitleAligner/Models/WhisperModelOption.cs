namespace SubtitleAligner.Models;

public sealed record WhisperModelOption(
    string Id,
    string DisplayName,
    string FileName,
    bool IsStandard,
    long MinimumBytes = 1,
    string? DownloadUrl = null)
{
    public override string ToString() => global::SubtitleAligner.Localization.T(DisplayName);
}

public static class WhisperModelCatalog
{
    public static IReadOnlyList<WhisperModelOption> OptionalModels { get; } =
    [
        new("small", "Small — opciono", "ggml-small.bin", false, 460_000_000,
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin?download=1"),
        new("medium", "Medium — opciono", "ggml-medium.bin", false, 1_500_000_000,
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin?download=1"),
        new("large-v3", "Large v3 — opciono", "ggml-large-v3.bin", false, 3_000_000_000,
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin?download=1")
    ];
}
