namespace SubtitleAligner.Models;

public enum BatchJobStatus
{
    Waiting,
    Processing,
    Completed,
    Failed,
    Skipped,
    Cancelled
}

public sealed class BatchJob
{
    public string Name { get; set; } = string.Empty;
    public string SourceMediaPath { get; set; } = string.Empty;
    public string SourceSubtitlePath { get; set; } = string.Empty; // TXT ulaz; naziv zadržan radi kompatibilnosti queue fajlova.
    public string MediaPath { get; set; } = string.Empty;
    public string SubtitlePath { get; set; } = string.Empty; // TXT ulaz za postojeći parser.
    public string OutputSrtPath { get; set; } = string.Empty;
    public string ProjectDirectory { get; set; } = string.Empty;
    public string ModelFileName { get; set; } = "ggml-tiny.bin";
    public string ModelDisplayName { get; set; } = "Tiny — brzo";
    public string Language { get; set; } = "sr-Latn";
    public string LanguageDisplayName { get; set; } = "Srpski - latinica (sr-Latn)";
    public BatchJobStatus Status { get; set; } = BatchJobStatus.Waiting;
    public int Progress { get; set; }
    public string StatusText { get; set; } = "Čeka";
    public bool ExportVtt { get; set; } = true;
    public bool ExportSbv { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
