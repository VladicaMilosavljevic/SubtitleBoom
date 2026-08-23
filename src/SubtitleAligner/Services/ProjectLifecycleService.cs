using System.Text.Json;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public static class ProjectLifecycleService
{
    public sealed class ProjectDocument
    {
        public int Version { get; set; } = 8;
        public string ProjectAppVersion { get; set; } = "4.1.1";
        public int ProjectFormatVersion { get; set; } = 8;
        public string? MediaPath { get; set; }
        public string? SrtPath { get; set; }
        public int SelectedIndex { get; set; }
        public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
        public long PlayerPositionMilliseconds { get; set; }
        public string? ModelFileName { get; set; }
        public string? Language { get; set; }
        public string ToleranceProfile { get; set; } = "Standardna";
        public double CustomStrongLimitSeconds { get; set; } = 0.45;
        public double CustomModerateLimitSeconds { get; set; } = 1.00;
        public long InitialProcessingMilliseconds { get; set; }
        public DateTime? InitialProcessedAtUtc { get; set; }
        public bool CreatedByBatch { get; set; }
        public Dictionary<string, long> ProcessingPhaseMilliseconds { get; set; } = new();
        public DateTime ProjectCreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastOpenedAtUtc { get; set; }
        public long LastLoadMilliseconds { get; set; }
        public long TotalLoadMilliseconds { get; set; }
        public int OpenCount { get; set; }
        public DateTime? LastEditedAtUtc { get; set; }
        public int ManualEditCount { get; set; }
        public int ApplyCount { get; set; }
        public int AutoSaveCount { get; set; }
        public List<int> ModifiedRows { get; set; } = new();
        public List<ProjectCueDocument> Cues { get; set; } = new();
    }

    public sealed class ProjectCueDocument
    {
        public long StartMilliseconds { get; set; }
        public long EndMilliseconds { get; set; }
        public string? Text { get; set; }
        public long? SpeechStartMilliseconds { get; set; }
        public long? SpeechEndMilliseconds { get; set; }
        public long? StartOffsetMilliseconds { get; set; }
        public long? EndOffsetMilliseconds { get; set; }
        public double Confidence { get; set; }
        public string? OpeningPhraseStatus { get; set; }
    }

    public static void SaveInitialProject(
        string outputSrtPath,
        string mediaPath,
        string modelFileName,
        string language,
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<ReviewSignal> signals,
        TimeSpan processingTime,
        bool createdByBatch,
        IReadOnlyDictionary<string, TimeSpan>? phaseDurations = null)
    {
        var document = new ProjectDocument
        {
            MediaPath = mediaPath,
            SrtPath = outputSrtPath,
            ModelFileName = modelFileName,
            Language = language,
            InitialProcessingMilliseconds = (long)processingTime.TotalMilliseconds,
            InitialProcessedAtUtc = DateTime.UtcNow,
            CreatedByBatch = createdByBatch,
            ProcessingPhaseMilliseconds = phaseDurations?.ToDictionary(item => item.Key, item => (long)item.Value.TotalMilliseconds, StringComparer.OrdinalIgnoreCase) ?? new(),
            Cues = cues.Select((cue, index) =>
            {
                ReviewSignal? signal = index < signals.Count ? signals[index] : null;
                return new ProjectCueDocument
                {
                    StartMilliseconds = (long)cue.Start.TotalMilliseconds,
                    EndMilliseconds = (long)cue.End.TotalMilliseconds,
                    Text = cue.Text,
                    SpeechStartMilliseconds = signal?.DetectedSpeechStart is TimeSpan ss ? (long)ss.TotalMilliseconds : null,
                    SpeechEndMilliseconds = signal?.DetectedSpeechEnd is TimeSpan se ? (long)se.TotalMilliseconds : null,
                    StartOffsetMilliseconds = signal?.DetectedSpeechStart is TimeSpan ds ? (long)(ds - cue.Start).TotalMilliseconds : null,
                    EndOffsetMilliseconds = signal?.DetectedSpeechEnd is TimeSpan de ? (long)(de - cue.End).TotalMilliseconds : null,
                    Confidence = signal?.OpeningPhraseConfidence ?? 0,
                    OpeningPhraseStatus = signal?.OpeningPhraseStatus
                };
            }).ToList()
        };

        string path = WorkspacePaths.GetProjectPath(outputSrtPath);
        WorkspacePaths.EnsureParentDirectory(path);
        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }
}
