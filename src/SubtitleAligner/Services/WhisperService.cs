using System.Text.Json;
using SubtitleAligner.Models;
using SubtitleAligner.Utilities;

namespace SubtitleAligner.Services;

public sealed class WhisperService(string whisperPath)
{
    public async Task<List<RecognizedWord>> RecognizeAsync(
        string wavPath,
        string modelPath,
        string language,
        string outputBasePath,
        Action<string> log,
        CancellationToken cancellationToken,
        bool translateToEnglish = false)
    {
        string safeModelPath = WhisperModelPathBridge.Resolve(modelPath, log);
        var arguments = new List<string>
        {
            "-m", safeModelPath,
            "-f", wavPath,
            "-l", language,
            "-of", outputBasePath,
            "-ojf",
            "-sow",
            "-t", Math.Max(1, Environment.ProcessorCount - 1).ToString()
        };
        if (translateToEnglish) arguments.Add("-tr");

        await ProcessRunner.RunAsync(
            whisperPath,
            arguments,
            log,
            cancellationToken);

        string jsonPath = outputBasePath + ".json";
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("whisper.cpp nije napravio JSON rezultat.", jsonPath);

        await using FileStream stream = File.OpenRead(jsonPath);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("transcription", out JsonElement transcription))
            throw new InvalidDataException("JSON rezultat nema polje 'transcription'.");

        var words = new List<RecognizedWord>();

        foreach (JsonElement segment in transcription.EnumerateArray())
        {
            long segmentStartMs = ReadOffset(segment, "from");

            if (!segment.TryGetProperty("tokens", out JsonElement tokens))
                continue;

            foreach (JsonElement token in tokens.EnumerateArray())
            {
                string text = token.TryGetProperty("text", out JsonElement textElement)
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty;

                if (text.StartsWith("[_", StringComparison.Ordinal))
                    continue;

                string normalized = TextNormalizer.NormalizeWord(text);
                bool punctuationOnly = text.Trim().Length > 0 &&
                    text.Trim().All(c => char.IsPunctuation(c) || char.IsSymbol(c));
                if (normalized.Length == 0 && !punctuationOnly)
                    continue;

                long from = ReadOffset(token, "from");
                long to = ReadOffset(token, "to");

                // Neke whisper.cpp verzije daju token-offsete relativno u segmentu.
                if (from < segmentStartMs)
                {
                    from += segmentStartMs;
                    to += segmentStartMs;
                }

                double probability = token.TryGetProperty("p", out JsonElement probabilityElement)
                    ? probabilityElement.GetDouble()
                    : 0.0;

                words.Add(new RecognizedWord(
                    text,
                    normalized,
                    TimeSpan.FromMilliseconds(Math.Max(0, from)),
                    TimeSpan.FromMilliseconds(Math.Max(from, to)),
                    probability));
            }
        }

        if (words.Count == 0)
            throw new InvalidDataException("Nije pronađena nijedna prepoznata reč.");

        return words;
    }

    private static long ReadOffset(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty("offsets", out JsonElement offsets) ||
            !offsets.TryGetProperty(name, out JsonElement value))
            return 0;

        return value.GetInt64();
    }
}
