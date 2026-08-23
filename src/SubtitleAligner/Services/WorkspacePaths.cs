namespace SubtitleAligner.Services;

/// <summary>
/// Centralno mesto za sve interne fajlove SubtitleBooma.
/// Završni SRT ostaje pored videa, dok cache, projekat, izveštaji i segmenti
/// odlaze u SubtitleBoom_Data podfoldere.
/// </summary>
public static class WorkspacePaths
{
    public const string WorkspaceFolderName = "SubtitleBoom_Data";

    public static string GetWorkspaceRoot(string outputSrtPath)
    {
        string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputSrtPath))
            ?? AppContext.BaseDirectory;
        return Path.Combine(outputDirectory, WorkspaceFolderName);
    }

    public static string GetCachePath(string outputSrtPath)
        => Path.Combine(GetWorkspaceRoot(outputSrtPath), "Cache",
            Path.GetFileNameWithoutExtension(outputSrtPath) + ".subtitlecache.json");

    public static string GetProjectPath(string outputSrtPath)
        => Path.Combine(GetWorkspaceRoot(outputSrtPath), "Project",
            Path.GetFileNameWithoutExtension(outputSrtPath) + ".subtitleproject.json");

    public static string GetReviewPath(string outputSrtPath)
        => Path.Combine(GetWorkspaceRoot(outputSrtPath), "Reports",
            Path.GetFileNameWithoutExtension(outputSrtPath) + "_REVIEW.txt");

    public static string GetPerformancePath(string outputSrtPath)
        => Path.Combine(GetWorkspaceRoot(outputSrtPath), "Reports",
            Path.GetFileNameWithoutExtension(outputSrtPath) + "_PERFORMANCE.txt");

    public static string GetSegmentsDirectory(string outputSrtPath)
        => Path.Combine(GetWorkspaceRoot(outputSrtPath), "Segments",
            Path.GetFileNameWithoutExtension(outputSrtPath) + ".segments");

    public static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    public static string GetLegacyCachePath(string outputSrtPath)
        => Path.ChangeExtension(outputSrtPath, ".subtitlecache.json");

    public static string GetLegacyProjectPath(string outputSrtPath)
        => Path.ChangeExtension(outputSrtPath, ".subtitleproject.json");

    public static string GetLegacyReviewPath(string outputSrtPath)
    {
        string directory = Path.GetDirectoryName(outputSrtPath) ?? AppContext.BaseDirectory;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(outputSrtPath) + "_REVIEW.txt");
    }

    public static string GetLegacyPerformancePath(string outputSrtPath)
    {
        string directory = Path.GetDirectoryName(outputSrtPath) ?? AppContext.BaseDirectory;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(outputSrtPath) + "_PERFORMANCE.txt");
    }

    public static string GetLegacySegmentsDirectory(string outputSrtPath)
    {
        string directory = Path.GetDirectoryName(outputSrtPath) ?? AppContext.BaseDirectory;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(outputSrtPath) + ".segments");
    }

    /// <summary>
    /// Vraća novu putanju. Ako postoji samo stari fajl/folder, pokušava da ga
    /// premesti u novi workspace; ako premeštanje ne uspe, koristi stari sadržaj.
    /// </summary>
    public static string ResolveAndMigrateFile(string newPath, string legacyPath)
    {
        if (File.Exists(newPath)) return newPath;
        if (!File.Exists(legacyPath)) return newPath;

        try
        {
            EnsureParentDirectory(newPath);
            File.Move(legacyPath, newPath, true);
            return newPath;
        }
        catch
        {
            return legacyPath;
        }
    }

    public static string ResolveAndMigrateDirectory(string newPath, string legacyPath)
    {
        if (Directory.Exists(newPath)) return newPath;
        if (!Directory.Exists(legacyPath)) return newPath;

        try
        {
            EnsureParentDirectory(newPath);
            Directory.Move(legacyPath, newPath);
            return newPath;
        }
        catch
        {
            return legacyPath;
        }
    }
}
