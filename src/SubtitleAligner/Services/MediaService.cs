namespace SubtitleAligner.Services;

public sealed class MediaService(string ffmpegPath)
{
    public async Task PrepareAudioAsync(
        string mediaPath,
        string wavPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (Path.GetExtension(mediaPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            log("Ulaz je WAV; proveravam i pretvaram ga u standardni 16 kHz mono format.");
        }
        else
        {
            log("Izdvajam zvuk iz video/audio fajla.");
        }

        await ProcessRunner.RunAsync(
            ffmpegPath,
            [
                "-hide_banner",
                "-loglevel", "warning",
                "-y",
                "-i", mediaPath,
                "-vn",
                "-ac", "1",
                "-ar", "16000",
                "-c:a", "pcm_s16le",
                wavPath
            ],
            log,
            cancellationToken);
    }
}
