using System.Text.Json;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public sealed class AnalysisCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string GetCachePath(string outputSrtPath)
        => WorkspacePaths.GetCachePath(outputSrtPath);

    public async Task<List<RecognizedWord>?> TryLoadWordsAsync(
        string outputSrtPath,
        string mediaPath,
        string modelPath,
        string language,
        CancellationToken token)
    {
        string cachePath = WorkspacePaths.ResolveAndMigrateFile(
            GetCachePath(outputSrtPath),
            WorkspacePaths.GetLegacyCachePath(outputSrtPath));
        if (!File.Exists(cachePath) || !File.Exists(mediaPath)) return null;

        try
        {
            await using FileStream stream = File.OpenRead(cachePath);
            CacheFile? cache = await JsonSerializer.DeserializeAsync<CacheFile>(stream, JsonOptions, token);
            if (cache is null || cache.Version < 1 || cache.Words.Count == 0) return null;

            FileInfo media = new(mediaPath);
            if (!string.Equals(Path.GetFullPath(cache.MediaPath ?? string.Empty), Path.GetFullPath(mediaPath), StringComparison.OrdinalIgnoreCase)) return null;
            if (cache.MediaSize != media.Length || cache.MediaLastWriteUtcTicks != media.LastWriteTimeUtc.Ticks) return null;
            if (!string.Equals(cache.ModelFileName, Path.GetFileName(modelPath), StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(cache.Language, language, StringComparison.OrdinalIgnoreCase)) return null;

            return cache.Words.Select(w => new RecognizedWord(
                w.Text ?? string.Empty,
                w.Normalized ?? string.Empty,
                TimeSpan.FromMilliseconds(w.StartMilliseconds),
                TimeSpan.FromMilliseconds(w.EndMilliseconds),
                w.Probability)).ToList();
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveWordsAsync(
        string outputSrtPath,
        string mediaPath,
        string modelPath,
        string language,
        IReadOnlyList<RecognizedWord> words,
        CancellationToken token)
    {
        FileInfo media = new(mediaPath);
        var cache = new CacheFile
        {
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            MediaPath = Path.GetFullPath(mediaPath),
            MediaSize = media.Length,
            MediaLastWriteUtcTicks = media.LastWriteTimeUtc.Ticks,
            ModelFileName = Path.GetFileName(modelPath),
            Language = language,
            Words = words.Select(w => new CachedWord
            {
                Text = w.Text,
                Normalized = w.Normalized,
                StartMilliseconds = (long)w.Start.TotalMilliseconds,
                EndMilliseconds = (long)w.End.TotalMilliseconds,
                Probability = w.Probability
            }).ToList()
        };

        string cachePath = GetCachePath(outputSrtPath);
        string? directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = cachePath + ".tmp";
        await using (FileStream stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, token);
        File.Move(temporary, cachePath, true);
    }

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? MediaPath { get; set; }
        public long MediaSize { get; set; }
        public long MediaLastWriteUtcTicks { get; set; }
        public string? ModelFileName { get; set; }
        public string? Language { get; set; }
        public List<CachedWord> Words { get; set; } = new();
    }

    private sealed class CachedWord
    {
        public string? Text { get; set; }
        public string? Normalized { get; set; }
        public long StartMilliseconds { get; set; }
        public long EndMilliseconds { get; set; }
        public double Probability { get; set; }
    }
}
