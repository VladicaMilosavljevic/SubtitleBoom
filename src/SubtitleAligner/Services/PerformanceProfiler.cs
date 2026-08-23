using System.Diagnostics;
using System.Text;

namespace SubtitleAligner.Services;

public sealed class PerformanceProfiler
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Dictionary<string, TimeSpan> _durations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Stopwatch> _running = new(StringComparer.OrdinalIgnoreCase);

    public bool CacheUsed { get; set; }
    public int CueCount { get; set; }
    public int WordCount { get; set; }
    public string ModelName { get; set; } = "";
    public string Language { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public TimeSpan TotalElapsed => _total.Elapsed;
    public IReadOnlyDictionary<string, TimeSpan> Durations => _durations;

    public Dictionary<string, long> GetDurationsMilliseconds()
        => _durations.ToDictionary(item => item.Key, item => (long)item.Value.TotalMilliseconds, StringComparer.OrdinalIgnoreCase);

    public void Start(string phase)
    {
        if (_running.ContainsKey(phase))
            return;
        _running[phase] = Stopwatch.StartNew();
    }

    public void Stop(string phase)
    {
        if (!_running.Remove(phase, out var stopwatch))
            return;
        stopwatch.Stop();
        _durations[phase] = stopwatch.Elapsed;
    }

    public TimeSpan Get(string phase) => _durations.TryGetValue(phase, out var value) ? value : TimeSpan.Zero;

    public void Finish()
    {
        foreach (var phase in _running.Keys.ToList())
            Stop(phase);
        _total.Stop();
    }

    public string BuildText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Localization.T("SUBTITLEBOOM v1.0 — PERFORMANCE REPORT"));
        sb.AppendLine(new string('=', 58));
        sb.AppendLine($"{Localization.T("Datum")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{Localization.T("Media")}: {MediaPath}");
        sb.AppendLine($"{Localization.T("Model")}: {ModelName}");
        sb.AppendLine($"{Localization.T("Jezik")}: {Language}");
        sb.AppendLine($"{Localization.T("Cache")}: {(CacheUsed ? Localization.T("KORIŠĆEN — Whisper preskočen") : Localization.T("NIJE KORIŠĆEN — puna prva obrada"))}");
        sb.AppendLine($"{Localization.T("Titlova")}: {CueCount}");
        sb.AppendLine($"{Localization.T("Prepoznatih reči")}: {WordCount}");
        sb.AppendLine();
        sb.AppendLine(Localization.T("FAZE OBRADE"));
        sb.AppendLine(new string('-', 58));

        foreach (var name in new[]
        {
            "Čitanje i segmentacija titlova",
            "Provera cache-a",
            "Izdvajanje audio-zapisa",
            "Whisper prepoznavanje",
            "Whisper segmentirana obrada",
            "Čuvanje cache-a",
            "Alignment",
            "Review i upis fajlova"
        })
        {
            if (_durations.TryGetValue(name, out var duration))
                sb.AppendLine($"{Localization.T(name),-34} {Format(duration),12}");
        }

        sb.AppendLine(new string('-', 58));
        sb.AppendLine($"{Localization.T("UKUPNO"),-34} {Format(_total.Elapsed),12}");
        sb.AppendLine();

        var slowest = _durations.OrderByDescending(x => x.Value).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(slowest.Key))
        {
            double percent = _total.Elapsed.TotalMilliseconds <= 0
                ? 0
                : slowest.Value.TotalMilliseconds / _total.Elapsed.TotalMilliseconds * 100.0;
            sb.AppendLine($"{Localization.T("Najsporija faza")}: {Localization.T(slowest.Key)} ({Format(slowest.Value)}, {percent:0.0}% {Localization.T("ukupnog vremena")})");
        }

        sb.AppendLine();
        sb.AppendLine(Localization.T("Napomena: Ovaj izveštaj služi da sledeća optimizacija bude zasnovana na merenju, a ne nagađanju."));
        return sb.ToString();
    }

    public async Task SaveAsync(string path, CancellationToken token)
    {
        Finish();
        await File.WriteAllTextAsync(path, BuildText(), new UTF8Encoding(false), token);
    }

    private static string Format(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return value.ToString(@"hh\:mm\:ss\.fff");
        return value.ToString(@"mm\:ss\.fff");
    }
}
