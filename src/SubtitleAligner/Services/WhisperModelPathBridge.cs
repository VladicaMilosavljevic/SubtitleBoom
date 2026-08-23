using System.Security.Cryptography;
using System.Text;

namespace SubtitleAligner.Services;

internal static class WhisperModelPathBridge
{
    public static string Resolve(string modelPath, Action<string> log)
    {
        if (modelPath.All(c => c <= 127)) return modelPath;

        var info = new FileInfo(modelPath);
        if (!info.Exists) return modelPath;

        string signature = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..16];
        string root = Path.Combine(Path.GetTempPath(), "SubtitleBoom", "ModelBridge", hash);
        Directory.CreateDirectory(root);
        string bridgedPath = Path.Combine(root, Path.GetFileName(modelPath));

        if (!File.Exists(bridgedPath) || new FileInfo(bridgedPath).Length != info.Length)
        {
            log(Localization.IsSerbian
                ? "Putanja modela sadrži Unicode znakove; pripremam bezbednu kopiju modela za Whisper…"
                : "The model path contains Unicode characters; preparing a safe model copy for Whisper…");
            File.Copy(modelPath, bridgedPath, overwrite: true);
        }

        return bridgedPath;
    }
}
