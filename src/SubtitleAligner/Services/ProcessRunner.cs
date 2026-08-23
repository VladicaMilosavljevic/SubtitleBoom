using System.Diagnostics;
using System.Text;

namespace SubtitleAligner.Services;

public static class ProcessRunner
{
    public static async Task RunAsync(
        string executable,
        IEnumerable<string> arguments,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException(Localization.IsSerbian
                ? $"Ne mogu da pokrenem: {executable}"
                : $"Unable to start: {executable}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(Localization.IsSerbian
                ? $"{Path.GetFileName(executable)} je završio greškom ({process.ExitCode})."
                : $"{Path.GetFileName(executable)} exited with error ({process.ExitCode}).");
    }
}
