using SubtitleAligner.Models;

namespace SubtitleAligner.Services;

public sealed class BatchProcessingService
{
    private readonly string _ffmpeg;
    private readonly string _whisper;
    private readonly string _modelsDirectory;

    public BatchProcessingService(string runtimeDirectory)
    {
        _ffmpeg = Path.Combine(runtimeDirectory, "bin", "ffmpeg.exe");
        _whisper = Path.Combine(runtimeDirectory, "bin", "whisper-cli.exe");
        _modelsDirectory = Path.Combine(runtimeDirectory, "models");
    }

    public async Task ProcessAsync(
        BatchJob job,
        Action<int, string> report,
        Action<string> log,
        CancellationToken token)
    {
        Validate(job);
        string model = Path.Combine(_modelsDirectory, job.ModelFileName);
        RequireFile(_ffmpeg, "Nedostaje FFmpeg.");
        RequireFile(_whisper, "Nedostaje whisper.cpp.");
        RequireFile(model, "Nedostaje izabrani Whisper model.");
        ProcessingLanguageOption languageOption = ProcessingLanguages.ByCode(job.Language);

        Directory.CreateDirectory(Path.GetDirectoryName(job.OutputSrtPath)!);
        var profiler = new PerformanceProfiler
        {
            MediaPath = job.MediaPath,
            ModelName = job.ModelDisplayName,
            Language = job.Language
        };

        string performancePath = WorkspacePaths.ResolveAndMigrateFile(
            WorkspacePaths.GetPerformancePath(job.OutputSrtPath),
            WorkspacePaths.GetLegacyPerformancePath(job.OutputSrtPath));
        WorkspacePaths.EnsureParentDirectory(performancePath);

        string work = Path.Combine(Path.GetTempPath(), "SubtitleBoom", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string wav = Path.Combine(work, "audio.wav");

        try
        {
            profiler.Start("Čitanje i segmentacija titlova");
            report(5, "Čitam TXT tekst…");
            var loadedCues = await SubtitleParser.LoadAsync(job.SubtitlePath, token);
            var cues = new SubtitleSegmenter().Segment(loadedCues);
            profiler.Stop("Čitanje i segmentacija titlova");
            profiler.CueCount = cues.Count;
            if (cues.Count == 0)
                throw new InvalidDataException("TXT nema upotrebljiv tekst.");

            var cacheService = new AnalysisCacheService();
            profiler.Start("Provera cache-a");
            List<RecognizedWord>? words = await cacheService.TryLoadWordsAsync(
                job.OutputSrtPath, job.MediaPath, model, job.Language, token);
            profiler.Stop("Provera cache-a");

            if (words is not null)
            {
                profiler.CacheUsed = true;
                report(72, "Cache pronađen — Whisper je preskočen.");
                log("CACHE: korišćena je ranije sačuvana analiza.");
            }
            else
            {
                profiler.CacheUsed = false;
                profiler.Start("Izdvajanje audio-zapisa");
                report(10, "Izdvajam audio-zapis…");
                await new MediaService(_ffmpeg).PrepareAudioAsync(job.MediaPath, wav, log, token);
                profiler.Stop("Izdvajanje audio-zapisa");

                profiler.Start("Whisper segmentirana obrada");
                report(20, "Prepoznajem govor u blokovima od 5 minuta…");
                string checkpointDirectory = WorkspacePaths.ResolveAndMigrateDirectory(
                    WorkspacePaths.GetSegmentsDirectory(job.OutputSrtPath),
                    WorkspacePaths.GetLegacySegmentsDirectory(job.OutputSrtPath));
                Directory.CreateDirectory(checkpointDirectory);

                words = await new SegmentedWhisperService(_ffmpeg, _whisper).RecognizeAsync(
                    wav,
                    model,
                    languageOption.WhisperCode,
                    work,
                    checkpointDirectory,
                    log,
                    (current, total, reused) =>
                    {
                        int progress = 20 + (int)Math.Round(current * 52d / Math.Max(1, total));
                        report(progress, $"{(reused ? "Učitavam" : "Obrađujem")} segment {current} od {total}…");
                    },
                    token);
                profiler.Stop("Whisper segmentirana obrada");

                profiler.Start("Čuvanje cache-a");
                report(75, "Čuvam Whisper cache…");
                await cacheService.SaveWordsAsync(job.OutputSrtPath, job.MediaPath, model, job.Language, words, token);
                profiler.Stop("Čuvanje cache-a");
            }

            profiler.WordCount = words.Count;
            profiler.Start("Alignment");
            report(82, "Poravnavam titlove…");
            var reviewSignals = new AlignmentService().Align(cues, words, job.Language);
            profiler.Stop("Alignment");

            profiler.Start("Review i upis fajlova");
            report(92, "Čuvam SRT i izveštaje…");
            string reviewPath = WorkspacePaths.ResolveAndMigrateFile(
                WorkspacePaths.GetReviewPath(job.OutputSrtPath),
                WorkspacePaths.GetLegacyReviewPath(job.OutputSrtPath));
            WorkspacePaths.EnsureParentDirectory(reviewPath);
            await SrtWriter.SaveAsync(job.OutputSrtPath, cues, token);
            string exportBase = Path.Combine(Path.GetDirectoryName(job.OutputSrtPath)!, Path.GetFileNameWithoutExtension(job.OutputSrtPath));
            if (job.ExportVtt) await SubtitleFormatWriter.SaveAsync(exportBase + ".vtt", cues, token);
            if (job.ExportSbv) await SubtitleFormatWriter.SaveAsync(exportBase + ".sbv", cues, token);
            await ManualReviewWriter.SaveAsync(reviewPath, cues, reviewSignals, token);
            profiler.Stop("Review i upis fajlova");
            await profiler.SaveAsync(performancePath, token);
            ProjectLifecycleService.SaveInitialProject(
                job.OutputSrtPath, job.MediaPath, job.ModelFileName, job.Language,
                cues, reviewSignals, profiler.TotalElapsed, createdByBatch: true, profiler.Durations);
            report(100, "Završeno — projekat i cache su spremni za trenutno otvaranje");
        }
        catch
        {
            profiler.Finish();
            try { await File.WriteAllTextAsync(performancePath, profiler.BuildText(), CancellationToken.None); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static void Validate(BatchJob job)
    {
        if (!File.Exists(job.MediaPath)) throw new FileNotFoundException("Video/audio nije pronađen.", job.MediaPath);
        if (!File.Exists(job.SubtitlePath)) throw new FileNotFoundException("TXT nije pronađen.", job.SubtitlePath);
        if (!string.Equals(Path.GetExtension(job.SubtitlePath), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Batch Mode koristi TXT kao ulaz. SRT se pravi kao rezultat obrade.");
        if (string.IsNullOrWhiteSpace(job.OutputSrtPath)) throw new InvalidDataException("Izlazni SRT nije određen.");
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(message + Environment.NewLine + path);
    }
}
