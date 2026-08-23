using SubtitleAligner.Models;

namespace SubtitleAligner;

internal static class ProjectDashboardForm
{
    public static void ShowDashboard(
        Form owner,
        string mediaPath,
        string outputSrtPath,
        string projectPath,
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<ReviewSignal> signals,
        int modifiedCount,
        bool hasUnsavedChanges,
        string language,
        string model,
        long initialProcessingMilliseconds,
        DateTime? initialProcessedAtUtc,
        bool createdByBatch,
        string toleranceProfile,
        IReadOnlyDictionary<string, long> processingPhaseMilliseconds,
        DateTime projectCreatedAtUtc,
        DateTime? lastOpenedAtUtc,
        long lastLoadMilliseconds,
        long totalLoadMilliseconds,
        int openCount,
        DateTime? lastEditedAtUtc,
        int manualEditCount,
        int applyCount,
        int autoSaveCount,
        int subtitleAddCount,
        int subtitleDeleteCount)
    {
        int total = cues.Count;
        int speechFound = signals.Count(s => s.DetectedSpeechStart.HasValue);
        int speechMissing = signals.Count(s => !s.DetectedSpeechStart.HasValue);
        int strong = signals.Count(s => string.Equals(s.OpeningPhraseStatus, "STRONG", StringComparison.OrdinalIgnoreCase));
        int moderate = signals.Count(s => string.Equals(s.OpeningPhraseStatus, "MODERATE", StringComparison.OrdinalIgnoreCase));
        int weak = signals.Count(s => string.Equals(s.OpeningPhraseStatus, "WEAK", StringComparison.OrdinalIgnoreCase));
        int problems = signals.Count(s => !string.Equals(s.OpeningPhraseStatus, "STRONG", StringComparison.OrdinalIgnoreCase));
        int reliable = Math.Max(0, total - problems);
        int health = total == 0 ? 0 : (int)Math.Round(100.0 * ((strong * 1.0) + (moderate * 0.65) + (weak * 0.25)) / total);
        health = Math.Clamp(health, 0, 100);

        TimeSpan duration = total == 0 ? TimeSpan.Zero : cues.Max(c => c.End);
        DateTime? lastSaved = File.Exists(projectPath) ? File.GetLastWriteTime(projectPath) : null;

        using var form = new Form
        {
            Text = Localization.T("Pregled projekta"),
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(900, 760),
            MinimumSize = new Size(720, 560),
            Font = new Font("Segoe UI", 10f),
            ShowIcon = false,
            MaximizeBox = false
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = Path.GetFileNameWithoutExtension(mediaPath),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };
        var subtitle = new Label
        {
            Text = $"{Localization.T("Jezik:")} {DisplayLanguage(language)}    {Localization.T("Model:")} {model}    {Localization.T("Trajanje:")} {FormatDuration(duration)}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 14)
        };

        var healthPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
        healthPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        healthPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var healthLabel = new Label { Text = "Zdravlje projekta", AutoSize = true, Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold) };
        var healthValue = new Label { Text = $"{health}% — {HealthText(health)}", AutoSize = true, Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold), ForeColor = HealthColor(health) };
        var progress = new ProgressBar { Dock = DockStyle.Fill, Height = 24, Minimum = 0, Maximum = 100, Value = health, Margin = new Padding(0, 8, 0, 0) };
        healthPanel.Controls.Add(healthLabel, 0, 0);
        healthPanel.Controls.Add(healthValue, 1, 0);
        healthPanel.Controls.Add(progress, 0, 1);
        healthPanel.SetColumnSpan(progress, 2);

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 3, Margin = new Padding(0, 0, 0, 16) };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        AddStat(stats, 0, 0, "Ukupno titlova", total.ToString());
        AddStat(stats, 1, 0, "Pouzdano", reliable.ToString());
        AddStat(stats, 2, 0, "Za proveru", problems.ToString());
        AddStat(stats, 3, 0, "Govor nije nađen", speechMissing.ToString());
        AddStat(stats, 0, 1, "STRONG", strong.ToString());
        AddStat(stats, 1, 1, "MODERATE", moderate.ToString());
        AddStat(stats, 2, 1, "WEAK", weak.ToString());
        AddStat(stats, 3, 1, "Izmenjeni redovi", modifiedCount.ToString());
        AddStat(stats, 0, 2, "Govor pronađen", speechFound.ToString());
        AddStat(stats, 1, 2, "Stanje čuvanja", hasUnsavedChanges ? "Nesačuvane izmene" : "Sve sačuvano");
        AddStat(stats, 2, 2, "Poslednje čuvanje", lastSaved?.ToString("dd.MM.yyyy HH:mm") ?? "Nije sačuvan");
        AddStat(stats, 3, 2, "SRT postoji", File.Exists(outputSrtPath) ? "Da" : "Ne");

        var lifecycle = new GroupBox { Text = "Obrada i projekat", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
        var lifecycleLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 13 };
        lifecycleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lifecycleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddInfo(lifecycleLayout, 0, "Prva obrada:", initialProcessedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—");
        AddInfo(lifecycleLayout, 1, "Utrošeno vreme:", initialProcessingMilliseconds > 0 ? FormatDuration(TimeSpan.FromMilliseconds(initialProcessingMilliseconds)) : "—");
        AddInfo(lifecycleLayout, 2, "Način obrade:", createdByBatch ? "Batch Mode" : "Pojedinačna obrada");
        AddInfo(lifecycleLayout, 3, "Tolerancija statusa:", toleranceProfile);
        AddInfo(lifecycleLayout, 4, "Projekat kreiran:", projectCreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
        AddInfo(lifecycleLayout, 5, "Poslednje otvaranje:", lastOpenedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "—");
        AddInfo(lifecycleLayout, 6, "Poslednje učitavanje:", lastLoadMilliseconds > 0 ? FormatLoad(lastLoadMilliseconds) : "—");
        AddInfo(lifecycleLayout, 7, "Prosečno učitavanje:", openCount > 0 ? FormatLoad(totalLoadMilliseconds / Math.Max(1, openCount)) : "—");
        AddInfo(lifecycleLayout, 8, "Broj otvaranja:", openCount.ToString());
        AddInfo(lifecycleLayout, 9, "Poslednja izmena:", lastEditedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "—");
        AddInfo(lifecycleLayout, 10, "Ručne izmene / Apply:", $"{manualEditCount} / {applyCount}");
        AddInfo(lifecycleLayout, 11, "Dodati / obrisani titlovi:", $"{subtitleAddCount} / {subtitleDeleteCount}");
        AddInfo(lifecycleLayout, 12, "Auto Save upisi:", autoSaveCount.ToString());
        lifecycle.Controls.Add(lifecycleLayout);
        var performance = new GroupBox { Text = "Vreme po fazama", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
        var performanceLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        performanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        performanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        int phaseRow = 0;
        foreach (string phase in new[] { "Čitanje i segmentacija titlova", "Provera cache-a", "Izdvajanje audio-zapisa", "Whisper segmentirana obrada", "Whisper prepoznavanje", "Čuvanje cache-a", "Alignment", "Review i upis fajlova" })
        {
            if (!processingPhaseMilliseconds.TryGetValue(phase, out long milliseconds) || milliseconds <= 0) continue;
            AddInfo(performanceLayout, phaseRow++, Localization.T(phase) + ":", FormatDuration(TimeSpan.FromMilliseconds(milliseconds)));
        }
        if (phaseRow == 0) AddInfo(performanceLayout, phaseRow, "Podaci:", "Nisu dostupni za stariji projekat");
        performance.Controls.Add(performanceLayout);


        var files = new GroupBox { Text = "Lokacije projekta", Dock = DockStyle.Fill, Padding = new Padding(12) };
        var fileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, AutoScroll = true };
        fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddPath(fileLayout, 0, "Media:", mediaPath);
        AddPath(fileLayout, 1, "SRT:", outputSrtPath);
        AddPath(fileLayout, 2, "Project:", projectPath);
        AddPath(fileLayout, 3, "Workspace:", Path.GetDirectoryName(projectPath) ?? "—");
        files.Controls.Add(fileLayout);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = Localization.T("Zatvori"), AutoSize = true, Padding = new Padding(18, 4, 18, 4) };
        var openFolder = new Button { Text = Localization.T("Otvori folder projekta"), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        close.Click += (_, _) => form.Close();
        openFolder.Click += (_, _) =>
        {
            try
            {
                string folder = Path.GetDirectoryName(outputSrtPath) ?? Path.GetDirectoryName(mediaPath) ?? string.Empty;
                if (Directory.Exists(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, ex.Message, "SubtitleBoom", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(openFolder);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(healthPanel, 0, 2);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        content.RowCount = 4;
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(stats, 0, 0);
        content.Controls.Add(lifecycle, 0, 1);
        content.Controls.Add(performance, 0, 2);
        content.Controls.Add(files, 0, 3);
        root.Controls.Add(content, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        form.Controls.Add(root);
        form.AcceptButton = close;
        form.CancelButton = close;
        Localization.Apply(form);
        form.ShowDialog(owner);
    }

    private static void AddStat(TableLayoutPanel parent, int column, int row, string label, string value)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 72, Margin = new Padding(4), BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window };
        var valueLabel = new Label { Text = value, Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold) };
        var nameLabel = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, ForeColor = SystemColors.GrayText };
        panel.Controls.Add(nameLabel);
        panel.Controls.Add(valueLabel);
        parent.Controls.Add(panel, column, row);
    }

    private static void AddInfo(TableLayoutPanel parent, int row, string label, string value)
    {
        parent.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), Margin = new Padding(0, 4, 12, 4) }, 0, row);
        parent.Controls.Add(new Label { Text = value, AutoSize = true, Margin = new Padding(0, 4, 0, 4) }, 1, row);
    }

    private static void AddPath(TableLayoutPanel parent, int row, string label, string path)
    {
        parent.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), Margin = new Padding(0, 6, 8, 6) }, 0, row);
        var value = new TextBox { Text = path, ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = SystemColors.Control, Margin = new Padding(0, 6, 0, 6) };
        parent.Controls.Add(value, 1, row);
    }

    private static string DisplayLanguage(string code) => Localization.T(ProcessingLanguages.ByCode(code).DisplayName);

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    private static string FormatLoad(long milliseconds) => milliseconds < 1000
        ? $"{milliseconds} ms"
        : $"{milliseconds / 1000.0:0.00} s";
    private static string HealthText(int value)
    {
        string source = value >= 90 ? "Odlično" : value >= 75 ? "Dobro" : value >= 55 ? "Potrebna provera" : "Slabo";
        return Localization.T(source);
    }
    private static Color HealthColor(int value) => value >= 90 ? Color.ForestGreen : value >= 75 ? Color.DarkGoldenrod : value >= 55 ? Color.DarkOrange : Color.Firebrick;
}
