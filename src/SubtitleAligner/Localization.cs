using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SubtitleAligner;

internal static class Localization
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleBoom");
    private static readonly string SettingPath = Path.Combine(SettingsDirectory, "interface_language.txt");
    private static Dictionary<string, string>? _translations;
    private static string? _currentCode;

    public static IReadOnlyList<(string Code, string Name)> AvailableLanguages { get; } =
    [
        ("auto", "Automatski / Automatic"),
        ("sr", "Srpski — latinica"),
        ("en", "English"),
        ("hr", "Hrvatski"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("es", "Español"),
        ("it", "Italiano"),
        ("pt", "Português"),
        ("ru", "Русский"),
        ("zh", "中文"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("ar", "العربية"),
        ("tr", "Türkçe"),
        ("pl", "Polski")
    ];

    public static string CurrentCode => _currentCode ??= ResolveLanguageCode();
    public static bool IsSerbian => string.Equals(CurrentCode, "sr", StringComparison.OrdinalIgnoreCase);

    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source) || CurrentCode == "sr") return source;
        EnsureLoaded();
        return _translations!.TryGetValue(source, out string? translated) ? translated : source;
    }

    public static string Status(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || IsSerbian) return source;
        string translated = T(source);
        if (!string.Equals(translated, source, StringComparison.Ordinal)) return translated;

        var exact = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Čeka"] = "Waiting",
            ["Pripremam obradu…"] = "Preparing processing…",
            ["Završeno"] = "Completed",
            ["Otkazano — može se nastaviti"] = "Canceled — can be resumed",
            ["Čeka ponovno pokretanje"] = "Waiting to restart",
            ["Čitam TXT tekst…"] = "Reading TXT text…",
            ["Cache pronađen — Whisper je preskočen."] = "Cache found — Whisper was skipped.",
            ["Izdvajam audio-zapis…"] = "Extracting audio…",
            ["Prepoznajem govor u blokovima od 5 minuta…"] = "Recognizing speech in 5-minute blocks…",
            ["Čuvam Whisper cache…"] = "Saving Whisper cache…",
            ["Poravnavam titlove…"] = "Aligning subtitles…",
            ["Čuvam SRT i izveštaje…"] = "Saving SRT and reports…",
            ["Završeno — projekat i cache su spremni za trenutno otvaranje"] = "Completed — project and cache are ready for immediate opening",
            ["Ulaz je WAV; proveravam i pretvaram ga u standardni 16 kHz mono format."] = "The input is WAV; checking and converting it to standard 16 kHz mono format.",
            ["Izdvajam zvuk iz video/audio fajla."] = "Extracting audio from the video/audio file.",
            ["CACHE: korišćena je ranije sačuvana analiza."] = "CACHE: a previously saved analysis was used.",
            ["Izdvajam zvuk…"] = "Extracting audio…",
            ["Tiny/Base prepoznaje govor…"] = "Tiny/Base is recognizing speech…",
            ["Detekcija govora je završena i sačuvana u projektu."] = "Speech detection is complete and saved in the project.",
            ["Detekcija govora nije uspela."] = "Speech detection failed.",
            ["Media fajl nije pronađen; uređivanje titlova je i dalje dostupno."] = "Media file was not found; subtitle editing is still available.",
            ["Editor je otvoren. Plejer se učitava u pozadini…"] = "Editor is open. The player is loading in the background…",
            ["Izmena je primenjena i projekat je automatski sačuvan."] = "The change was applied and the project was saved automatically.",
            ["Titl već ima odgovarajući prelom redova."] = "The subtitle already has appropriate line breaks.",
            ["Poslednja izmena je poništena."] = "The last change was undone.",
            ["Izmena je ponovljena."] = "The change was redone.",
            ["YouTube paket je izvezen: SRT, VTT i SBV."] = "YouTube package exported: SRT, VTT and SBV.",
            ["Titl je podeljen na dva reda i projekat je automatski sačuvan."] = "The subtitle was split into two rows and the project was saved automatically.",
            ["Titl je spojen sa sledećim i projekat je automatski sačuvan."] = "The subtitle was merged with the next one and the project was saved automatically.",
            ["Novi red je dodat i projekat je automatski sačuvan."] = "A new row was added and the project was saved automatically.",
            ["Red je obrisan i projekat je automatski sačuvan."] = "The row was deleted and the project was saved automatically.",
            ["Preuzimanje je otkazano."] = "Download was canceled.",
            ["Preuzimanje nije uspelo."] = "Download failed."
        };
        if (exact.TryGetValue(source, out string? value)) return value;

        Match segment = Regex.Match(source, @"^(Obrađujem|Učitavam) segment (\d+) od (\d+)…$");
        if (segment.Success)
            return $"{(segment.Groups[1].Value == "Učitavam" ? "Loading" : "Processing")} segment {segment.Groups[2].Value} of {segment.Groups[3].Value}…";
        Match neighbor = Regex.Match(source, @"^(Prethodni|Sledeći) titl #(\d+) je izabran za grafičko uređivanje\.$");
        if (neighbor.Success)
            return $"{(neighbor.Groups[1].Value == "Prethodni" ? "Previous" : "Next")} subtitle #{neighbor.Groups[2].Value} was selected for waveform editing.";
        Match completedSegment = Regex.Match(source, @"^SEGMENT (\d+)/(\d+): završen i sačuvan \((\d+) Whisper tokena\)\.$");
        if (completedSegment.Success)
            return $"SEGMENT {completedSegment.Groups[1].Value}/{completedSegment.Groups[2].Value}: completed and saved ({completedSegment.Groups[3].Value} Whisper tokens).";
        Match reusedSegment = Regex.Match(source, @"^SEGMENT (\d+)/(\d+): već završen — učitavam sačuvani rezultat\.$");
        if (reusedSegment.Success)
            return $"SEGMENT {reusedSegment.Groups[1].Value}/{reusedSegment.Groups[2].Value}: already completed — loading the saved result.";
        Match missingModel = Regex.Match(source, @"^Model nije pronađen za posao '(.+)'\.$");
        if (missingModel.Success)
            return $"Model was not found for job '{missingModel.Groups[1].Value}'.";

        return source
            .Replace("Plejer je postavljen na titl #", "Player was set to subtitle #", StringComparison.Ordinal)
            .Replace("Trenutno vreme je kopirano:", "Current time copied:", StringComparison.Ordinal)
            .Replace("Upisano trenutno vreme plejera:", "Current player time entered:", StringComparison.Ordinal)
            .Replace("Plejer je postavljen na ", "Player was set to ", StringComparison.Ordinal)
            .Replace("Editor radi, ali plejer nije mogao da se pokrene:", "The editor is running, but the player could not start:", StringComparison.Ordinal)
            .Replace("Preview titla nije mogao odmah da se osveži:", "The subtitle preview could not refresh immediately:", StringComparison.Ordinal)
            .Replace("Detekcija govora nije uspela:", "Speech detection failed:", StringComparison.Ordinal)
            .Replace("Vreme nije moglo da se kopira:", "The time could not be copied:", StringComparison.Ordinal)
            .Replace("Auto Save nije uspeo:", "Auto Save failed:", StringComparison.Ordinal)
            .Replace("Sačuvani projekat nije mogao da se učita:", "The saved project could not be loaded:", StringComparison.Ordinal)
            .Replace("Projekat je učitan. Nastavak rada: titl #", "Project loaded. Resume: subtitle #", StringComparison.Ordinal)
            .Replace("Ručna tolerancija je primenjena: POUZDANO", "Custom tolerance applied: RELIABLE", StringComparison.Ordinal)
            .Replace("PROVERITI", "REVIEW", StringComparison.Ordinal)
            .Replace("Sačuvano:", "Saved:", StringComparison.Ordinal)
            .Replace("Nije pronađen odgovarajući video za TXT:", "No matching video was found for TXT:", StringComparison.Ordinal)
            .Replace("ZAVRŠENO:", "COMPLETED:", StringComparison.Ordinal)
            .Replace("Otkazano. Završeni segmenti su sačuvani.", "Canceled. Completed segments were saved.", StringComparison.Ordinal)
            .Replace("whisper.cpp nije napravio JSON rezultat.", "whisper.cpp did not create a JSON result.", StringComparison.Ordinal)
            .Replace("JSON rezultat nema polje 'transcription'.", "The JSON result does not contain a 'transcription' field.", StringComparison.Ordinal)
            .Replace("Nije pronađena nijedna prepoznata reč.", "No recognized words were found.", StringComparison.Ordinal)
            .Replace("Preuzimam ", "Downloading ", StringComparison.Ordinal)
            .Replace(" je instaliran.", " is installed.", StringComparison.Ordinal)
            .Replace("GREŠKA:", "ERROR:", StringComparison.Ordinal);
    }

    public static string Filter(string filter)
    {
        string[] parts = filter.Split('|');
        for (int i = 0; i < parts.Length; i += 2) parts[i] = T(parts[i]);
        return string.Join('|', parts);
    }

    public static void Apply(Control root)
    {
        TranslateControl(root);
        foreach (Control child in root.Controls) Apply(child);
    }

    public static void SetLanguage(string code)
    {
        if (!AvailableLanguages.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))) return;
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingPath, code);
        _currentCode = null;
        _translations = null;
    }

    private static void TranslateControl(Control control)
    {
        control.Text = T(control.Text ?? string.Empty);

        if (control is DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = T(column.HeaderText);
            if (grid.ContextMenuStrip is not null) TranslateToolStripItems(grid.ContextMenuStrip.Items);
        }
        if (control is MenuStrip menu) TranslateToolStripItems(menu.Items);
        if (control is StatusStrip status) TranslateToolStripItems(status.Items);
    }

    private static void TranslateToolStripItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.Text = T(item.Text ?? string.Empty);
            if (item is ToolStripDropDownItem dropDown) TranslateToolStripItems(dropDown.DropDownItems);
        }
    }

    private static string ResolveLanguageCode()
    {
        try
        {
            if (File.Exists(SettingPath))
            {
                string saved = File.ReadAllText(SettingPath).Trim().ToLowerInvariant();
                if (saved != "auto" && AvailableLanguages.Any(item => item.Code == saved)) return saved;
            }
        }
        catch { }

        string windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return AvailableLanguages.Any(item => item.Code == windows) ? windows : "en";
    }

    private static void EnsureLoaded()
    {
        if (_translations is not null) return;

        // English is the safe fallback for international interfaces, but the
        // selected language must override it wherever a translation exists.
        try
        {
            var merged = LoadLanguage("en");
            string code = CurrentCode;
            if (!string.Equals(code, "en", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in LoadLanguage(code))
                    merged[pair.Key] = pair.Value;
            }
            _translations = merged;
        }
        catch { _translations = new(); }
    }

    private static Dictionary<string, string> LoadLanguage(string code)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "languages", code + ".json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new()
            : new();
    }
}
