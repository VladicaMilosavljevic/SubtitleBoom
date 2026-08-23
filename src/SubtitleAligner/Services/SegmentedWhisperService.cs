using System.Text.Json;
using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

/// <summary>
/// Obrađuje dugačke WAV fajlove u sekvencijalnim blokovima i čuva svaki gotov
/// blok na disku. Ako se obrada prekine, sledeće pokretanje nastavlja od prvog
/// segmenta koji još nije sačuvan.
/// </summary>
public sealed class SegmentedWhisperService(string ffmpegPath, string whisperPath)
{
    private const int SegmentSeconds = 300;
    private const int OverlapMilliseconds = 2000;

    public async Task<List<RecognizedWord>> RecognizeAsync(
        string wavPath,
        string modelPath,
        string language,
        string workDirectory,
        string checkpointDirectory,
        Action<string> log,
        Action<int, int, bool>? segmentProgress,
        CancellationToken cancellationToken,
        bool translateToEnglish = false)
    {
        long durationMilliseconds = ReadPcmWavDurationMilliseconds(wavPath);
        if (durationMilliseconds <= 0)
            throw new InvalidDataException("Ne mogu da odredim trajanje pripremljenog WAV fajla.");

        int segmentCount = Math.Max(1, (int)Math.Ceiling(durationMilliseconds / (SegmentSeconds * 1000d)));
        Directory.CreateDirectory(checkpointDirectory);
        Directory.CreateDirectory(workDirectory);

        string manifestPath = Path.Combine(checkpointDirectory, "manifest.json");
        await EnsureManifestAsync(
            manifestPath,
            wavPath,
            modelPath,
            language,
            durationMilliseconds,
            segmentCount,
            translateToEnglish,
            cancellationToken);

        var allWords = new List<RecognizedWord>();
        var whisper = new WhisperService(whisperPath);

        for (int index = 0; index < segmentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string segmentResultPath = Path.Combine(checkpointDirectory, $"segment_{index + 1:000}.json");
            List<RecognizedWord>? words = await TryLoadSegmentAsync(segmentResultPath, cancellationToken);

            if (words is not null)
            {
                log($"SEGMENT {index + 1}/{segmentCount}: već završen — učitavam sačuvani rezultat.");
                segmentProgress?.Invoke(index + 1, segmentCount, true);
                allWords.AddRange(words);
                continue;
            }

            segmentProgress?.Invoke(index + 1, segmentCount, false);

            long nominalStart = index * SegmentSeconds * 1000L;
            long nominalEnd = Math.Min(durationMilliseconds, nominalStart + SegmentSeconds * 1000L);
            long extractionStart = Math.Max(0, nominalStart - OverlapMilliseconds);
            long extractionEnd = Math.Min(durationMilliseconds, nominalEnd + OverlapMilliseconds);
            long extractionDuration = Math.Max(1, extractionEnd - extractionStart);

            string segmentWav = Path.Combine(workDirectory, $"segment_{index + 1:000}.wav");
            string outputBase = Path.Combine(workDirectory, $"segment_{index + 1:000}_recognition");

            log($"SEGMENT {index + 1}/{segmentCount}: {FormatTime(extractionStart)}–{FormatTime(extractionEnd)}");

            await ProcessRunner.RunAsync(
                ffmpegPath,
                [
                    "-hide_banner",
                    "-loglevel", "warning",
                    "-y",
                    "-ss", (extractionStart / 1000d).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    "-t", (extractionDuration / 1000d).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                    "-i", wavPath,
                    "-ac", "1",
                    "-ar", "16000",
                    "-c:a", "pcm_s16le",
                    segmentWav
                ],
                log,
                cancellationToken);

            List<RecognizedWord> localWords = await whisper.RecognizeAsync(
                segmentWav,
                modelPath,
                language,
                outputBase,
                log,
                cancellationToken,
                translateToEnglish);

            // Vraćamo lokalna vremena segmenta na apsolutnu poziciju u originalu.
            words = localWords
                .Select(word => new RecognizedWord(
                    word.Text,
                    word.Normalized,
                    word.Start + TimeSpan.FromMilliseconds(extractionStart),
                    word.End + TimeSpan.FromMilliseconds(extractionStart),
                    word.Probability))
                // Svaki segment ima mali preklop. Zadržavamo samo reči čiji centar
                // pripada nominalnom delu segmenta, pa nema duplikata na granicama.
                .Where(word =>
                {
                    double center = (word.Start.TotalMilliseconds + word.End.TotalMilliseconds) / 2d;
                    bool afterStart = index == 0 || center >= nominalStart;
                    bool beforeEnd = index == segmentCount - 1 || center < nominalEnd;
                    return afterStart && beforeEnd;
                })
                .ToList();

            await SaveSegmentAsync(segmentResultPath, words, cancellationToken);
            allWords.AddRange(words);

            TryDelete(segmentWav);
            TryDelete(outputBase + ".json");
            log($"SEGMENT {index + 1}/{segmentCount}: završen i sačuvan ({words.Count} Whisper tokena).");
        }

        List<RecognizedWord> merged = allWords
            .OrderBy(word => word.Start)
            .ThenBy(word => word.End)
            .ToList();

        if (merged.Count == 0)
            throw new InvalidDataException("Segmentirana Whisper obrada nije pronašla nijednu reč.");

        return merged;
    }

    private static long ReadPcmWavDurationMilliseconds(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF") return 0;
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") return 0;

        int byteRate = 0;
        long dataSize = 0;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            long next = stream.Position + chunkSize + (chunkSize % 2);

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                reader.ReadInt16(); // audio format
                reader.ReadInt16(); // channels
                reader.ReadInt32(); // sample rate
                byteRate = reader.ReadInt32();
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
            }

            if (byteRate > 0 && dataSize > 0) break;
            stream.Position = Math.Min(next, stream.Length);
        }

        return byteRate > 0 ? (long)Math.Round(dataSize * 1000d / byteRate) : 0;
    }

    private static async Task EnsureManifestAsync(
        string manifestPath,
        string wavPath,
        string modelPath,
        string language,
        long durationMilliseconds,
        int segmentCount,
        bool translateToEnglish,
        CancellationToken cancellationToken)
    {
        var expected = new SegmentManifest(
            Version: 3,
            WavLength: new FileInfo(wavPath).Length,
            DurationMilliseconds: durationMilliseconds,
            ModelFileName: Path.GetFileName(modelPath),
            Language: language,
            SegmentSeconds: SegmentSeconds,
            SegmentCount: segmentCount,
            TranslateToEnglish: translateToEnglish);

        if (File.Exists(manifestPath))
        {
            try
            {
                SegmentManifest? existing = JsonSerializer.Deserialize<SegmentManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken));

                if (existing == expected)
                    return;
            }
            catch { }

            foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(manifestPath)!, "segment_*.json"))
                TryDelete(file);
        }

        string json = JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
    }

    private static async Task<List<RecognizedWord>?> TryLoadSegmentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;

        try
        {
            SegmentWordDto[]? data = JsonSerializer.Deserialize<SegmentWordDto[]>(
                await File.ReadAllTextAsync(path, cancellationToken));
            if (data is null) return null;

            return data.Select(item => new RecognizedWord(
                item.Text,
                item.Normalized,
                TimeSpan.FromMilliseconds(item.StartMilliseconds),
                TimeSpan.FromMilliseconds(item.EndMilliseconds),
                item.Probability)).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveSegmentAsync(
        string path,
        IReadOnlyList<RecognizedWord> words,
        CancellationToken cancellationToken)
    {
        SegmentWordDto[] data = words.Select(word => new SegmentWordDto(
            word.Text,
            word.Normalized,
            (long)Math.Round(word.Start.TotalMilliseconds),
            (long)Math.Round(word.End.TotalMilliseconds),
            word.Probability)).ToArray();

        string temp = path + ".tmp";
        string json = JsonSerializer.Serialize(data);
        await File.WriteAllTextAsync(temp, json, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    private static string FormatTime(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record SegmentManifest(
        int Version,
        long WavLength,
        long DurationMilliseconds,
        string ModelFileName,
        string Language,
        int SegmentSeconds,
        int SegmentCount,
        bool TranslateToEnglish);

    private sealed record SegmentWordDto(
        string Text,
        string Normalized,
        long StartMilliseconds,
        long EndMilliseconds,
        double Probability);
}
