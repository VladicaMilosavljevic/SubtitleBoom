namespace SubtitleAligner.Models;

public sealed record ProcessingLanguageOption(
    string Code,
    string DisplayName,
    string? WhisperLanguageCode = null,
    string? SerbianScriptMode = null)
{
    public string WhisperCode => WhisperLanguageCode ?? Code;
    public string LocalizedName
    {
        get
        {
            string key = $"ProcessingLanguage.{Code}";
            string localized = global::SubtitleAligner.Localization.T(key);

            if (string.Equals(localized, key, StringComparison.Ordinal))
                localized = global::SubtitleAligner.Localization.T(DisplayName);

            return localized;
        }
    }

    public override string ToString()
    {
        return $"{LocalizedName} ({Code})";
    }
}

public static class ProcessingLanguages
{
    private static readonly ProcessingLanguageOption Automatic = new("auto", "Automatsko prepoznavanje", "auto");

    private static readonly IReadOnlyList<ProcessingLanguageOption> Languages = new List<ProcessingLanguageOption>
    {
        new("sr-Latn", "Srpski - latinica", "sr", "Latinica"),
        new("sr-Cyrl", "Srpski - ćirilica", "sr", "Ćirilica"),
        new("hr", "Hrvatski"),
        new("en", "Engleski"),
        new("af", "Afrikans"),
        new("sq", "Albanski"),
        new("am", "Amharski"),
        new("ar", "Arapski"),
        new("hy", "Jermenski"),
        new("as", "Asamski"),
        new("az", "Azerbejdžanski"),
        new("ba", "Baškirski"),
        new("eu", "Baskijski"),
        new("be", "Beloruski"),
        new("bn", "Bengalski"),
        new("bs", "Bosanski"),
        new("br", "Bretonski"),
        new("bg", "Bugarski"),
        new("my", "Burmanski / mijanmarski"),
        new("ca", "Katalonski"),
        new("zh", "Kineski"),
        new("cs", "Češki"),
        new("da", "Danski"),
        new("nl", "Holandski"),
        new("et", "Estonski"),
        new("fo", "Farski"),
        new("fi", "Finski"),
        new("fr", "Francuski"),
        new("gl", "Galicijski"),
        new("ka", "Gruzijski"),
        new("de", "Nemački"),
        new("el", "Grčki"),
        new("gu", "Gudžarati"),
        new("ht", "Haićanski kreolski"),
        new("ha", "Hausa"),
        new("haw", "Havajski"),
        new("he", "Hebrejski"),
        new("hi", "Hindi"),
        new("hu", "Mađarski"),
        new("is", "Islandski"),
        new("id", "Indonežanski"),
        new("it", "Italijanski"),
        new("ja", "Japanski"),
        new("jw", "Javanski"),
        new("kn", "Kanada"),
        new("kk", "Kazaški"),
        new("km", "Kmerski"),
        new("ko", "Korejski"),
        new("lo", "Laoški"),
        new("la", "Latinski"),
        new("lv", "Letonski"),
        new("ln", "Lingala"),
        new("lt", "Litvanski"),
        new("lb", "Luksemburški"),
        new("mk", "Makedonski"),
        new("mg", "Malgaški"),
        new("ms", "Malajski"),
        new("ml", "Malajalam"),
        new("mt", "Malteški"),
        new("mi", "Maorski"),
        new("mr", "Marati"),
        new("mn", "Mongolski"),
        new("ne", "Nepalski"),
        new("no", "Norveški"),
        new("nn", "Norveški - nynorsk"),
        new("oc", "Oksitanski"),
        new("ps", "Paštunski"),
        new("fa", "Persijski"),
        new("pl", "Poljski"),
        new("pt", "Portugalski"),
        new("pa", "Pandžapski"),
        new("ro", "Rumunski"),
        new("ru", "Ruski"),
        new("sa", "Sanskrit"),
        new("sn", "Šona"),
        new("sd", "Sindi"),
        new("si", "Sinhalski"),
        new("sk", "Slovački"),
        new("sl", "Slovenački"),
        new("so", "Somalijski"),
        new("es", "Španski"),
        new("su", "Sundanski"),
        new("sw", "Svahili"),
        new("sv", "Švedski"),
        new("tl", "Tagalog"),
        new("tg", "Tadžički"),
        new("ta", "Tamilski"),
        new("tt", "Tatarski"),
        new("te", "Telugu"),
        new("th", "Tajlandski"),
        new("bo", "Tibetanski"),
        new("tr", "Turski"),
        new("tk", "Turkmenski"),
        new("uk", "Ukrajinski"),
        new("ur", "Urdu"),
        new("uz", "Uzbečki"),
        new("vi", "Vijetnamski"),
        new("cy", "Velški"),
        new("yi", "Jidiš"),
        new("yo", "Joruba"),
    };

    public static IReadOnlyList<ProcessingLanguageOption> All =>
        new[] { Automatic }
            .Concat(Languages.OrderBy(
                language => language.LocalizedName,
                StringComparer.CurrentCultureIgnoreCase))
            .ToArray();

    public static ProcessingLanguageOption ByCode(string? code)
    {
        if (string.Equals(code, "sr", StringComparison.OrdinalIgnoreCase)) code = "sr-Latn";
        return All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }

    public static string CodeFromDisplay(string? display)
    {
        ProcessingLanguageOption? exact = All.FirstOrDefault(x =>
            string.Equals(x.ToString(), display, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.DisplayName, display, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Code;

        // Batch redovi ostaju čitljivi i kada se jezik interfejsa promeni,
        // jer se stabilni kod nalazi u završnim zagradama prikazanog naziva.
        if (!string.IsNullOrWhiteSpace(display))
        {
            int open = display.LastIndexOf('(');
            int close = display.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                string code = display[(open + 1)..close].Trim();
                ProcessingLanguageOption? byCode = All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                if (byCode is not null) return byCode.Code;
            }
        }
        return "auto";
    }
}
