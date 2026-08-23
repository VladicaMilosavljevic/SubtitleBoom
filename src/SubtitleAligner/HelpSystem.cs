using System.Diagnostics;
using System.Text;

namespace SubtitleAligner;

internal static class HelpSystem
{
    public const string ShortcutText = """
PREČICE NA TASTATURI

Reprodukcija
Space              Play / Pause
Esc                Stop

Navigacija
F3                 Sledeći problem
Shift + F3         Prethodni problem
Alt + ←            Prethodni titl
Alt + →            Sledeći titl
Home               Prvi titl
End                Poslednji titl

Uređivanje
Ctrl + Enter       Primeni izmenu
Ctrl + S           Sačuvaj ispravljeni SRT
Ctrl + Z           Undo
Ctrl + Y           Redo
Delete             Obriši izabrani titl

Pomoć
F1                 Prikaži ovaj pregled
F2                 Pregled projekta

Napomena: dok je kursor u polju za unos teksta, Space, Home i End zadržavaju svoje uobičajeno ponašanje.
""";


    private const string CustomToleranceText = """
PRILAGOĐENA TOLERANCIJA

U editoru izaberite Tolerancija statusa → Prilagođena.
Podesite granicu za POUZDANO i gornju granicu za PROVERITI.
Sve preko druge granice dobija status SLABO.

Promena je trenutna. Ne pokreće Whisper, ne pomera titlove i automatski se čuva u projektu.
Pređite mišem preko statusa u tabeli da vidite odstupanje početka, kraja i confidence.
""";

    private static string DocumentationDirectory => Path.Combine(AppContext.BaseDirectory, "docs");

    public static MenuStrip CreateMenu(Form owner)
    {
        var menu = new MenuStrip();
        var help = new ToolStripMenuItem("Pomoć");
        help.DropDownItems.Add("Brzi početak", null, (_, _) => OpenDocument(owner, "QUICK_START.txt", QuickStartText));
        help.DropDownItems.Add("Prečice na tastaturi (F1)", null, (_, _) => ShowShortcuts(owner));
        help.DropDownItems.Add("Korisničko uputstvo (PDF)", null, (_, _) => OpenDocument(owner, "SubtitleBoom_User_Guide.pdf", UserManualText));
        help.DropDownItems.Add("Korisničko uputstvo (tekst)", null, (_, _) => OpenDocument(owner, "USER_MANUAL.txt", UserManualText));
        help.DropDownItems.Add("Podržani formati titlova", null, (_, _) => OpenDocument(owner, "SUBTITLE_FORMATS.txt", "Uvoz: TXT, SRT, VTT, SBV, ASS/SSA i TTML/DFXP. Izvoz: SRT, VTT, SBV, ASS i TXT."));
        help.DropDownItems.Add("Responsive Editor", null, (_, _) => OpenDocument(owner, "RESPONSIVE_EDITOR_LAYOUT.txt", "Video, waveform i najvažnije komande su stalno vidljivi bez glavnog vertikalnog skrolovanja."));
        help.DropDownItems.Add("Batch Mode vodič", null, (_, _) => OpenDocument(owner, "BATCH_MODE_GUIDE.txt", BatchGuideText));
        help.DropDownItems.Add("Prilagođena tolerancija", null, (_, _) => OpenDocument(owner, "CUSTOM_TOLERANCE.txt", CustomToleranceText));
        help.DropDownItems.Add("Profesionalni alati editora", null, (_, _) => OpenDocument(owner, "PROFESSIONAL_EDITING_TOOLS.txt", "Dodavanje i brisanje titlova, Undo/Redo i automatsko čuvanje."));
        help.DropDownItems.Add("Šta je novo", null, (_, _) => OpenDocument(owner, "WHATS_NEW.txt", WhatsNewText));
        help.DropDownItems.Add(new ToolStripSeparator());
        var languageMenu = new ToolStripMenuItem("Jezik programa");
        foreach ((string code, string name) in Localization.AvailableLanguages)
        {
            string resolved = code == "auto" ? ResolveAutomaticLanguage() : code;
            var languageItem = new ToolStripMenuItem(name)
            {
                Checked = code == "auto"
                    ? !HasExplicitLanguageSetting()
                    : string.Equals(Localization.CurrentCode, resolved, StringComparison.OrdinalIgnoreCase) && HasExplicitLanguageSetting()
            };
            languageItem.Click += (_, _) =>
            {
                Localization.SetLanguage(code);
                MessageBox.Show(owner,
                    Localization.T("Jezik interfejsa je sačuvan. Ponovo pokrenite program da bi se promena primenila."),
                    Localization.T("Jezik programa"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            languageMenu.DropDownItems.Add(languageItem);
        }
        help.DropDownItems.Add(languageMenu);
        help.DropDownItems.Add("Podrži razvoj…", null, (_, _) => DonationService.Open(owner));
        help.DropDownItems.Add("O programu", null, (_, _) => ShowAbout(owner));
        menu.Items.Add(help);
        Localization.Apply(menu);
        return menu;
    }

    private static string ResolveAutomaticLanguage()
    {
        string windows = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return windows is "sr" or "hr" ? windows : "en";
    }

    private static bool HasExplicitLanguageSetting()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SubtitleBoom", "interface_language.txt");
        try { return File.Exists(path) && !string.Equals(File.ReadAllText(path).Trim(), "auto", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static void ShowShortcuts(Form owner) => ShowShortcutsDialog(owner);


    private static void ShowShortcutsDialog(Form owner)
    {
        using var form = new Form
        {
            Text = "Prečice na tastaturi",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(720, 650),
            MinimumSize = new Size(620, 520),
            Font = new Font("Segoe UI", 10f),
            KeyPreview = true,
            ShowIcon = false,
            MaximizeBox = false
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = SystemColors.Control
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Text = "Prečice na tastaturi",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        };

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            MultiSelect = false,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
        list.Columns.Add(Localization.T("Prečica"), 170, HorizontalAlignment.Left);
        list.Columns.Add(Localization.T("Funkcija"), 470, HorizontalAlignment.Left);

        AddShortcutGroup(list, "REPRODUKCIJA", new[]
        {
            ("Space", "Pokreni / pauziraj reprodukciju"),
            ("Esc", "Zaustavi reprodukciju")
        });
        AddShortcutGroup(list, "NAVIGACIJA KROZ PROBLEME", new[]
        {
            ("F3", "Sledeći problem"),
            ("Shift + F3", "Prethodni problem")
        });
        AddShortcutGroup(list, "NAVIGACIJA KROZ TITLOVE", new[]
        {
            ("Alt + ←", "Prethodni titl"),
            ("Alt + →", "Sledeći titl"),
            ("Home", "Prvi titl"),
            ("End", "Poslednji titl")
        });
        AddShortcutGroup(list, "UREĐIVANJE", new[]
        {
            ("Ctrl + Enter", "Primeni izmenu"),
            ("Ctrl + S", "Sačuvaj ispravljeni SRT"),
            ("Ctrl + Z", "Undo — poništi"),
            ("Ctrl + Y", "Redo — ponovi"),
            ("Delete", "Obriši izabrani titl")
        });
        AddShortcutGroup(list, "POMOĆ I PROJEKAT", new[]
        {
            ("F1", "Prikaži ili zatvori ovaj pregled"),
            ("F2", "Otvori pregled stanja projekta")
        });

        void ResizeColumns()
        {
            if (list.ClientSize.Width <= 0) return;
            list.Columns[0].Width = Math.Max(150, Math.Min(190, list.ClientSize.Width / 3));
            list.Columns[1].Width = Math.Max(300, list.ClientSize.Width - list.Columns[0].Width - 5);
        }
        list.Resize += (_, _) => ResizeColumns();

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Text = "Napomena: dok kucaš u polju za tekst, Space, Home i End zadržavaju svoje uobičajeno ponašanje.",
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 12, 0, 10)
        };

        var close = new Button
        {
            Text = "Zatvori (Esc)",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(18, 4, 18, 4)
        };
        close.Click += (_, _) => form.Close();
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1)
            {
                e.Handled = true;
                form.Close();
            }
        };
        form.Shown += (_, _) => ResizeColumns();

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(list, 0, 1);
        root.Controls.Add(note, 0, 2);
        root.Controls.Add(close, 0, 3);
        form.Controls.Add(root);
        form.AcceptButton = close;
        form.CancelButton = close;
        Localization.Apply(form);
        form.ShowDialog(owner);
    }

    private static void AddShortcutGroup(ListView list, string groupName, IEnumerable<(string Shortcut, string Action)> items)
    {
        var group = new ListViewGroup(Localization.T(groupName), HorizontalAlignment.Left);
        list.Groups.Add(group);
        foreach (var item in items)
        {
            var row = new ListViewItem(item.Shortcut, group);
            row.SubItems.Add(Localization.T(item.Action));
            list.Items.Add(row);
        }
    }

    public static void ShowAbout(Form owner)
    {
        MessageBox.Show(owner,
            Localization.T("SubtitleBoom omogućava lokalno poravnanje, transkripciju, prevod, pregled i uređivanje titlova.") + "\n\n" +
            Localization.T("Program je besplatan. Ako vam koristi, razvoj možete dobrovoljno podržati kroz Pomoć → Podrži razvoj.") + "\n\n" +
            Localization.T("SubtitleBoom koristi biblioteke projekta FFmpeg pod licencom LGPLv2.1."),
            Localization.T("O programu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static void ShowWelcomeOnce(Form owner)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleBoom");
            string marker = Path.Combine(dir, "v2.6_welcome_shown.txt");
            if (File.Exists(marker)) return;
            Directory.CreateDirectory(dir);
            MessageBox.Show(owner,
                Localization.IsSerbian
                    ? "Dobro došli u SubtitleBoom v1.0 — First Public Release!\n\nNajvažnije prečice:\nSpace — Play/Pause\nF3 — Sledeći problem\nCtrl+Enter — Primeni izmenu\nF1 — Sve prečice\n\nKompletna pomoć nalazi se u meniju Pomoć."
                    : "Welcome to SubtitleBoom v1.0 — First Public Release!\n\nMain shortcuts:\nSpace — Play/Pause\nF3 — Next problem\nCtrl+Enter — Apply change\nF1 — All shortcuts\n\nComplete help is available in the Help menu.",
                Localization.IsSerbian ? "Brzi početak" : "Quick start", MessageBoxButtons.OK, MessageBoxIcon.Information);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        catch { }
    }

    private static void OpenDocument(Form owner, string fileName, string fallback)
    {
        string documentationLanguage = Localization.CurrentCode == "sr" ? "sr" : "en";
        string localizedPath = Path.Combine(DocumentationDirectory, documentationLanguage, fileName);
        string path = File.Exists(localizedPath) ? localizedPath : Path.Combine(DocumentationDirectory, "en", fileName);
        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return;
            }
            catch { }
        }
        ShowText(owner, Path.GetFileNameWithoutExtension(fileName).Replace('_', ' '), fallback, new Size(760, 650));
    }

    private static void ShowText(Form owner, string title, string text, Size size)
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Size = size,
            MinimumSize = new Size(520, 420),
            Font = new Font("Segoe UI", 10f),
            KeyPreview = true
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = text,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(14)
        };
        var close = new Button { Text = "Zatvori (Esc)", Dock = DockStyle.Bottom, Height = 38 };
        close.Click += (_, _) => form.Close();
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1) form.Close(); };
        form.Controls.Add(box);
        form.Controls.Add(close);
        Localization.Apply(form);
        form.ShowDialog(owner);
    }

    private const string BatchGuideText = """
BATCH MODE — SAŽETAK

• Ulaz po poslu je video/audio + TXT.
• SRT nastaje kao rezultat obrade.
• Svaki projekat ostaje u svom postojećem folderu.
• Jezik i model biraju se posebno za svaki red.
• *_aligned.srt ostaje pored videa.
• Cache, projekat, izveštaji i segmenti idu u SubtitleBoom_Data.
• Originalni fajlovi se ne kopiraju, ne pomeraju i ne brišu.

Za kompletan primer pogledajte docs\BATCH_MODE_GUIDE.txt.
""";

    private const string QuickStartText = """
BRZI POČETAK

1. Izaberi video ili audio fajl.
2. Izaberi TXT ili SRT.
3. Izaberi izlazni SRT, jezik i Whisper model.
4. Klikni ALIGN — NAPRAVI SRT.
5. Posle obrade klikni UREDI TITLOVE.
6. U editoru koristi F3 za sledeći problem i Ctrl+Enter za primenu izmene.
7. Sačuvaj ispravljeni SRT pomoću Ctrl+S.

F1 u editoru uvek prikazuje kompletan spisak prečica.
""";

    private const string UserManualText = """
KORISNIČKO UPUTSTVO — SAŽETAK

SubtitleBoom poravnava postojeći tekst sa govorom u video ili audio fajlu. Tiny je brz i preporučen za poravnanje, dok Base pruža bolju ravnotežu brzine i preciznosti.

CACHE
Prva analiza novog snimka pokreće Whisper. Sledeća analiza istog snimka, modela i jezika koristi sačuvani cache i završava se mnogo brže.

SEGMENTIRANA OBRADA
Dugi snimci obrađuju se u blokovima. Završeni segmenti se čuvaju, pa prekinuta obrada može da se nastavi.

EDITOR
Levo je lista titlova, desno plejer i polja za uređivanje. Detektovani govor može da se kopira u početak ili ceo interval titla. Follow Playback automatski prati aktivni titl.

STATUSI
STRONG / POUZDANO — snažno podudaranje.
MODERATE / PROVERITI — vredi poslušati.
WEAK / SLABO — ručna provera je preporučena.

Originalni SRT se ne menja dok ne izabereš čuvanje.
""";

    private const string WhatsNewText = """
ŠTA JE NOVO — v2.6

• F1 pregled svih prečica.
• Space — Play/Pause bez konflikta sa unosom teksta.
• F3 / Shift+F3 — sledeći i prethodni problem.
• Alt+strelice — prethodni i sledeći titl.
• Home / End — prvi i poslednji titl.
• Ctrl+Enter — Primeni izmenu.
• Ctrl+S — Sačuvaj SRT.
• Ctrl+Z / Ctrl+Y — Undo / Redo.
• Esc — Stop.
• Novi meni Pomoć, tooltipovi i uvodni prozor.
• Dokumentacija je dostupna i offline.

Alignment, cache, segmentirana obrada i offline build nisu menjani.
""";
}
