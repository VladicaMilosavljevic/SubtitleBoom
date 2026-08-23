using SubtitleAligner.Services;
using SubtitleAligner.Models;

namespace SubtitleAligner;

public sealed class MainForm : Form
{
    private readonly TextBox _mediaBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _subtitleBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _languageBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems,
        Width = 245
    };
    private readonly ComboBox _modelBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly Button _additionalModelsButton = new() { Text = "Dodatni modeli…", AutoSize = true };
    private readonly Button _alignButton = new() { Text = "ALIGN — NAPRAVI SRT", Height = 48, Dock = DockStyle.Top };
    private readonly Button _transcribeButton = new() { Text = "TRANSKRIPCIJA / PREVOD IZ VIDEA ILI AUDIJA", Height = 44, AutoSize = true };
    private readonly CheckBox _translateToEnglishCheckBox = new() { Text = "Prevedi govor na engleski", AutoSize = true };
    private readonly CheckBox _timedTxtCheckBox = new() { Text = "Dodaj vremenske oznake u TXT", AutoSize = true, Checked = true };
    private readonly Button _cancelButton = new() { Text = "Otkaži", Enabled = false, Width = 90 };
    private readonly Button _editButton = new() { Text = "UREDI TITLOVE", Enabled = false, Width = 140 };
    private readonly Button _openEditorButton = new() { Text = "OTVORI SRT U EDITORU…", Width = 190 };
    private readonly Button _batchButton = new() { Text = "SERIJSKA OBRADA…", Width = 130 };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Style = ProgressBarStyle.Continuous };
    private readonly Label _status = new() { Text = "Spremno.", AutoSize = true };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f)
    };
    private readonly ToolTip _toolTips = new();

    private CancellationTokenSource? _cancellation;
    private List<SubtitleCue>? _editableCues;
    private List<ReviewSignal>? _reviewSignals;

    public MainForm()
    {
        Text = "SubtitleBoom v1.0";
        Width = 820;
        Height = 650;
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        _languageBox.Items.AddRange(ProcessingLanguages.All.Cast<object>().ToArray());
        _languageBox.SelectedItem = ProcessingLanguages.ByCode("auto");
        RefreshModelList("tiny");

        BuildLayout();
        Shown += (_, _) => HelpSystem.ShowWelcomeOnce(this);

        _alignButton.Click += async (_, _) => await StartAlignmentAsync();
        _transcribeButton.Click += async (_, _) => await StartTranscriptionAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
        _editButton.Click += (_, _) => OpenEditor();
        _openEditorButton.Click += async (_, _) => await OpenExistingSrtAsync();
        _additionalModelsButton.Click += (_, _) => OpenAdditionalModels();
        _batchButton.Click += (_, _) => OpenBatchManager();
        _modelBox.SelectedIndexChanged += (_, _) => UpdateModelGuidance();
        _languageBox.SelectedIndexChanged += (_, _) => UpdateTranscriptionOptions();
        _translateToEnglishCheckBox.CheckedChanged += (_, _) => UpdateTranscriptionOptions();
        UpdateTranscriptionOptions();
        UpdateModelGuidance();
        Localization.Apply(this);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            RowCount = 11,
            ColumnCount = 1,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "SubtitleBoom",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            AutoSize = true
        };
        var subtitle = new Label
        {
            Text = "Poravnanje, transkripcija i Whisper prevod na engleski — potpuno lokalno.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(FileRow("1. Video ili audio", _mediaBox, PickMedia));
        root.Controls.Add(FileRow("2. TXT ili SRT", _subtitleBox, PickSubtitle));
        root.Controls.Add(FileRow("3. Sačuvaj novi SRT", _outputBox, PickOutput));

        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        options.Controls.Add(new Label { Text = "Jezik:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        options.Controls.Add(_languageBox);
        options.Controls.Add(new Label { Text = "Model:", AutoSize = true, Margin = new Padding(24, 7, 6, 0) });
        options.Controls.Add(_modelBox);
        options.Controls.Add(_additionalModelsButton);
        root.Controls.Add(options);

        var transcriptionOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 10) };
        transcriptionOptions.Controls.Add(_transcribeButton);
        transcriptionOptions.Controls.Add(_translateToEnglishCheckBox);
        transcriptionOptions.Controls.Add(_timedTxtCheckBox);
        root.Controls.Add(transcriptionOptions);

        root.Controls.Add(_alignButton);
        root.Controls.Add(_progress);

        var statusRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        statusRow.Controls.Add(_status);
        statusRow.Controls.Add(_cancelButton);
        statusRow.Controls.Add(_editButton);
        statusRow.Controls.Add(_openEditorButton);
        statusRow.Controls.Add(_batchButton);
        root.Controls.Add(statusRow);
        root.Controls.Add(_log);

        var menu = HelpSystem.CreateMenu(this);
        MainMenuStrip = menu;
        Controls.Add(root);
        Controls.Add(menu);
    }

    private static Control FileRow(string label, TextBox box, Action pickAction)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 4, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));

        var button = new Button { Text = "Izaberi…", Dock = DockStyle.Fill };
        button.Click += (_, _) => pickAction();

        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(box, 1, 0);
        panel.Controls.Add(button, 2, 0);
        return panel;
    }

    private void PickMedia()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = Localization.Filter("Video i audio|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|Svi fajlovi|*.*")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _mediaBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(_outputBox.Text))
            _outputBox.Text = Path.Combine(
                Path.GetDirectoryName(dialog.FileName)!,
                Path.GetFileNameWithoutExtension(dialog.FileName) + "_aligned.srt");
    }

    private void PickSubtitle()
    {
        using var dialog = new OpenFileDialog { Filter = Localization.Filter(SubtitleParser.OpenFilter) };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _subtitleBox.Text = dialog.FileName;
    }

    private void PickOutput()
    {
        using var dialog = new SaveFileDialog { Filter = Localization.Filter(SubtitleFormatWriter.SaveFilter), DefaultExt = "srt", AddExtension = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputBox.Text = dialog.FileName;
    }

    private async Task StartAlignmentAsync()
    {
        var profiler = new PerformanceProfiler();
        string? performancePath = null;

        try
        {
            ValidateInputs();
            SetBusy(true);
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;

            string runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
            string ffmpeg = Path.Combine(runtime, "bin", "ffmpeg.exe");
            string whisper = Path.Combine(runtime, "bin", "whisper-cli.exe");
            if (_modelBox.SelectedItem is not WhisperModelOption selectedModel)
                throw new InvalidDataException("Izaberi Whisper model.");
            string model = Path.Combine(runtime, "models", selectedModel.FileName);

            RequireFile(ffmpeg, "Nedostaje FFmpeg.");
            RequireFile(whisper, "Nedostaje whisper.cpp.");
            RequireFile(model, "Nedostaje izabrani Whisper model.");

            ProcessingLanguageOption languageOption = _languageBox.SelectedItem as ProcessingLanguageOption
                ?? ProcessingLanguages.ByCode(ProcessingLanguages.CodeFromDisplay(_languageBox.Text));
            string language = languageOption.Code;
            string whisperLanguage = languageOption.WhisperCode;

            profiler.MediaPath = _mediaBox.Text;
            profiler.ModelName = selectedModel.DisplayName;
            profiler.Language = language;

            string outputDirectory = Path.GetDirectoryName(_outputBox.Text) ?? AppContext.BaseDirectory;
            performancePath = WorkspacePaths.ResolveAndMigrateFile(
                WorkspacePaths.GetPerformancePath(_outputBox.Text),
                WorkspacePaths.GetLegacyPerformancePath(_outputBox.Text));
            WorkspacePaths.EnsureParentDirectory(performancePath);

            string work = Path.Combine(Path.GetTempPath(), "SubtitleBoom", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string wav = Path.Combine(work, "audio.wav");

            profiler.Start("Čitanje i segmentacija titlova");
            Report(8, "Čitam titlove…");
            var loadedCues = await SubtitleParser.LoadAsync(_subtitleBox.Text, token);
            var cues = new SubtitleSegmenter().Segment(loadedCues);
            profiler.Stop("Čitanje i segmentacija titlova");
            profiler.CueCount = cues.Count;
            Log(Localization.IsSerbian
                ? $"Učitano: {loadedCues.Count}; posle formatiranja: {cues.Count} titlova."
                : $"Loaded: {loadedCues.Count}; after formatting: {cues.Count} subtitles.");
            if (cues.Count == 0)
                throw new InvalidDataException("TXT/SRT nema upotrebljiv tekst.");

            var cacheService = new AnalysisCacheService();
            profiler.Start("Provera cache-a");
            List<RecognizedWord>? words = await cacheService.TryLoadWordsAsync(
                _outputBox.Text, _mediaBox.Text, model, language, token);
            profiler.Stop("Provera cache-a");

            if (words is not null)
            {
                profiler.CacheUsed = true;
                Report(70, "Učitavam sačuvanu Whisper analizu…");
                Log(Localization.IsSerbian
                    ? $"CACHE: pronađena je važeća analiza ({words.Count} reči). FFmpeg i Whisper se preskaču."
                    : $"CACHE: a valid analysis was found ({words.Count} words). FFmpeg and Whisper are skipped.");
            }
            else
            {
                profiler.CacheUsed = false;
                profiler.Start("Izdvajanje audio-zapisa");
                Report(18, "Izdvajam zvuk iz videa…");
                await new MediaService(ffmpeg).PrepareAudioAsync(_mediaBox.Text, wav, Log, token);
                profiler.Stop("Izdvajanje audio-zapisa");

                profiler.Start("Whisper segmentirana obrada");
                Report(35, "Prepoznajem govor u blokovima od 5 minuta…");

                string checkpointDirectory = WorkspacePaths.ResolveAndMigrateDirectory(
                    WorkspacePaths.GetSegmentsDirectory(_outputBox.Text),
                    WorkspacePaths.GetLegacySegmentsDirectory(_outputBox.Text));
                Directory.CreateDirectory(checkpointDirectory);

                words = await new SegmentedWhisperService(ffmpeg, whisper).RecognizeAsync(
                    wav,
                    model,
                    whisperLanguage,
                    work,
                    checkpointDirectory,
                    Log,
                    (current, total, reused) =>
                    {
                        int progress = 30 + (int)Math.Round(current * 43d / Math.Max(1, total));
                        string action = Localization.IsSerbian
                            ? (reused ? "Učitavam" : "Obrađujem")
                            : (reused ? "Loading" : "Processing");
                        Report(progress, $"{action} segment {current} od {total}…");
                    },
                    token);
                profiler.Stop("Whisper segmentirana obrada");

                profiler.Start("Čuvanje cache-a");
                Report(76, "Čuvam Whisper analizu za sledeće otvaranje…");
                await cacheService.SaveWordsAsync(
                    _outputBox.Text, _mediaBox.Text, model, language, words, token);
                profiler.Stop("Čuvanje cache-a");
                Log((Localization.IsSerbian ? "CACHE: analiza je sačuvana u " : "CACHE: analysis saved to ") + cacheService.GetCachePath(_outputBox.Text));
            }

            profiler.WordCount = words.Count;

            profiler.Start("Alignment");
            Report(82, "Poravnavam tekst stabilnim v0.5 algoritmom…");
            var reviewSignals = new AlignmentService().Align(cues, words, language);
            profiler.Stop("Alignment");

            profiler.Start("Review i upis fajlova");
            Report(92, "Pravim listu mesta za ručnu proveru…");
            string reviewPath = WorkspacePaths.ResolveAndMigrateFile(
                WorkspacePaths.GetReviewPath(_outputBox.Text),
                WorkspacePaths.GetLegacyReviewPath(_outputBox.Text));
            WorkspacePaths.EnsureParentDirectory(reviewPath);

            Report(96, "Pravim SRT, REVIEW i PERFORMANCE izveštaj…");
            await SubtitleFormatWriter.SaveAsync(_outputBox.Text, cues, token);
            await ManualReviewWriter.SaveAsync(reviewPath, cues, reviewSignals, token);
            profiler.Stop("Review i upis fajlova");
            await profiler.SaveAsync(performancePath, token);
            ProjectLifecycleService.SaveInitialProject(
                _outputBox.Text, _mediaBox.Text, Path.GetFileName(model), language,
                cues, reviewSignals, profiler.TotalElapsed, createdByBatch: false, profiler.Durations);

            _editableCues = cues;
            _reviewSignals = reviewSignals;
            _editButton.Enabled = true;
            Log("Review notes: " + reviewPath);
            Log("Performance report: " + performancePath);
            Log(profiler.BuildText());

            Report(100, Localization.T("Gotovo."));
            MessageBox.Show(
                this,
                $"{(Localization.IsSerbian ? "Novi titl je sačuvan:" : "The new subtitle has been saved:")}\r\n\r\n{_outputBox.Text}\r\n\r\n{(Localization.IsSerbian ? "Pomoćni fajlovi su organizovani u:" : "Supporting files are stored in:")}\r\n{WorkspacePaths.GetWorkspaceRoot(_outputBox.Text)}",
                "SubtitleBoom",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            try { Directory.Delete(work, true); } catch { }
            OpenEditor();
        }
        catch (OperationCanceledException)
        {
            profiler.Finish();
            Report(0, "Obrada je otkazana.");
        }
        catch (Exception ex)
        {
            profiler.Finish();
            Log((Localization.IsSerbian ? "GREŠKA:" : "ERROR:") + " " + Localization.Status(ex.Message));
            if (!string.IsNullOrWhiteSpace(performancePath))
            {
                try
                {
                    await File.WriteAllTextAsync(
                        performancePath,
                        profiler.BuildText() + Environment.NewLine + "GREŠKA:" + Environment.NewLine + ex,
                        cancellationToken: CancellationToken.None);
                }
                catch { }
            }
            MessageBox.Show(this, Localization.Status(ex.Message), Localization.T("SubtitleBoom — greška"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Report(0, "Greška.");
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private async Task StartTranscriptionAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? work = null;
        try
        {
            if (!File.Exists(_mediaBox.Text))
                throw new FileNotFoundException("Izaberi postojeći video ili audio fajl.");
            if (_modelBox.SelectedItem is not WhisperModelOption selectedModel)
                throw new InvalidDataException("Izaberi Whisper model.");

            SetBusy(true);
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            string runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
            string ffmpeg = Path.Combine(runtime, "bin", "ffmpeg.exe");
            string whisper = Path.Combine(runtime, "bin", "whisper-cli.exe");
            string model = Path.Combine(runtime, "models", selectedModel.FileName);
            RequireFile(ffmpeg, "Nedostaje FFmpeg.");
            RequireFile(whisper, "Nedostaje whisper.cpp.");
            RequireFile(model, "Nedostaje izabrani Whisper model.");

            ProcessingLanguageOption languageOption = _languageBox.SelectedItem as ProcessingLanguageOption
                ?? ProcessingLanguages.ByCode(ProcessingLanguages.CodeFromDisplay(_languageBox.Text));
            string language = languageOption.Code;
            string whisperLanguage = languageOption.WhisperCode;
            bool translate = _translateToEnglishCheckBox.Checked;
            string scriptMode = translate ? "Automatski" : languageOption.SerbianScriptMode ?? "Automatski";
            string directory = Path.GetDirectoryName(_mediaBox.Text)!;
            string baseName = Path.GetFileNameWithoutExtension(_mediaBox.Text);
            string suffix = translate ? "_english" : "_transcribed";
            string srtPath = Path.Combine(directory, baseName + suffix + ".srt");
            string plainTxtPath = Path.Combine(directory, baseName + suffix + ".txt");
            string? timedTxtPath = _timedTxtCheckBox.Checked
                ? Path.Combine(directory, baseName + suffix + "_timed.txt")
                : null;

            string[] existingOutputs = new[] { srtPath, plainTxtPath, timedTxtPath }
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!)
                .ToArray();
            if (existingOutputs.Length > 0)
            {
                string message = (Localization.IsSerbian ? "Sledeći rezultat već postoji:\n\n" : "The following result already exists:\n\n") +
                    string.Join("\n", existingOutputs.Select(Path.GetFileName)) +
                    (Localization.IsSerbian ? "\n\nDa li želiš da ga zameniš novom obradom?" : "\n\nDo you want to replace it with a new result?");
                if (MessageBox.Show(this, message, Localization.IsSerbian ? "Postojeći rezultat" : "Existing result", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    Report(0, "Transkripcija nije pokrenuta.");
                    return;
                }
            }

            work = Path.Combine(Path.GetTempPath(), "SubtitleBoom", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string wav = Path.Combine(work, "audio.wav");
            Log(Localization.IsSerbian
                ? $"REŽIM: {(translate ? "PREVOD NA ENGLESKI" : "TRANSKRIPCIJA")} | Jezik: {languageOption.DisplayName} | Model: {selectedModel.DisplayName}"
                : $"MODE: {(translate ? "TRANSLATION TO ENGLISH" : "TRANSCRIPTION")} | Language: {Localization.T(languageOption.DisplayName)} | Model: {Localization.T(selectedModel.DisplayName)}");
            Report(10, "Izdvajam zvuk iz video/audio fajla…");
            await new MediaService(ffmpeg).PrepareAudioAsync(_mediaBox.Text, wav, Log, token);

            Report(25, translate ? "Whisper prevodi govor na engleski…" : "Whisper pravi transkripciju govora…");
            string checkpointDirectory = WorkspacePaths.GetSegmentsDirectory(srtPath);
            Directory.CreateDirectory(checkpointDirectory);
            List<RecognizedWord> words = await new SegmentedWhisperService(ffmpeg, whisper).RecognizeAsync(
                wav, model, whisperLanguage, work, checkpointDirectory, Log,
                (current, total, reused) =>
                {
                    int progress = 25 + (int)Math.Round(current * 55d / Math.Max(1, total));
                    Report(progress, Localization.IsSerbian
                        ? $"{(reused ? "Učitavam" : "Obrađujem")} segment {current} od {total}…"
                        : $"{(reused ? "Loading" : "Processing")} segment {current} of {total}…");
                }, token, translate);

            Report(84, "Formiram titlove i tekstualne fajlove…");
            var transcription = new TranscriptionService();
            List<SubtitleCue> cues = transcription.BuildCues(words, scriptMode);
            if (cues.Count == 0) throw new InvalidDataException("Whisper nije napravio upotrebljivu transkripciju.");
            List<ReviewSignal> signals = transcription.BuildSignals(cues, words);
            await SubtitleFormatWriter.SaveAsync(srtPath, cues, token);
            await transcription.SaveTextAsync(plainTxtPath, timedTxtPath, cues, token);

            stopwatch.Stop();
            ProjectLifecycleService.SaveInitialProject(
                srtPath, _mediaBox.Text, selectedModel.FileName, translate ? "en" : language,
                cues, signals, stopwatch.Elapsed, createdByBatch: false);
            _editableCues = cues;
            _reviewSignals = signals;
            _outputBox.Text = srtPath;
            _editButton.Enabled = true;
            Report(100, "Transkripcija je završena.");

            string files = $"SRT:\r\n{srtPath}\r\n\r\n{(Localization.IsSerbian ? "Običan TXT" : "Plain TXT")}:\r\n{plainTxtPath}";
            if (timedTxtPath is not null) files += $"\r\n\r\n{(Localization.IsSerbian ? "TXT sa vremenima" : "Timestamped TXT")}:\r\n{timedTxtPath}";
            MessageBox.Show(this, files, Localization.IsSerbian
                    ? (translate ? "Prevod je završen" : "Transkripcija je završena")
                    : (translate ? "Translation complete" : "Transcription complete"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenEditor();
        }
        catch (OperationCanceledException)
        {
            Report(0, "Obrada je otkazana.");
        }
        catch (Exception ex)
        {
            Log((Localization.IsSerbian ? "GREŠKA TRANSKRIPCIJE:" : "TRANSCRIPTION ERROR:") + " " + Localization.Status(ex.Message));
            MessageBox.Show(this, Localization.Status(ex.Message), Localization.T("Transkripcija — greška"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            Report(0, "Greška.");
        }
        finally
        {
            if (work is not null) { try { Directory.Delete(work, true); } catch { } }
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private void UpdateTranscriptionOptions()
    {
        bool translate = _translateToEnglishCheckBox.Checked;
        _transcribeButton.Text = translate
            ? Localization.T("PREVEDI GOVOR NA ENGLESKI")
            : Localization.T("NAPRAVI TRANSKRIPCIJU IZ VIDEA ILI AUDIJA");
    }

    private void UpdateModelGuidance()
    {
        if (_modelBox.SelectedItem is not WhisperModelOption model) return;
        string guidance = model.Id switch
        {
            "tiny" => Localization.T("Tiny — brzo: preporučen za poravnanje postojećeg teksta i brze obrade."),
            "base" => Localization.T("Base — standard: uravnotežen odnos brzine i preciznosti."),
            _ => Localization.IsSerbian
                ? $"{model.DisplayName}: preporučen za zahtevniju transkripciju i prevod, uz dužu obradu."
                : $"{model.DisplayName}: recommended for more demanding transcription and translation, with longer processing time."
        };
        _toolTips.SetToolTip(_modelBox, guidance);
        _toolTips.SetToolTip(_additionalModelsButton, Localization.IsSerbian
            ? "Small i jači modeli mogu poboljšati transkripciju i prevod, ali su sporiji."
            : "Small and larger models can improve transcription and translation, but they are slower.");
    }


    private async Task OpenExistingSrtAsync()
    {
        using var srtDialog = new OpenFileDialog
        {
            Filter = Localization.Filter(SubtitleParser.OpenFilter),
            Title = Localization.IsSerbian ? "Izaberi titl za uređivanje" : "Select a subtitle to edit"
        };
        if (srtDialog.ShowDialog(this) != DialogResult.OK) return;

        string mediaPath = File.Exists(_mediaBox.Text) ? _mediaBox.Text : string.Empty;
        using var mediaDialog = new OpenFileDialog
        {
            Filter = Localization.Filter("Video i audio|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|Svi fajlovi|*.*"),
            Title = Localization.IsSerbian ? "Izaberi video ili audio za pregled (Otkaži za uređivanje bez plejera)" : "Select video or audio for preview (Cancel to edit without the player)"
        };
        if (mediaDialog.ShowDialog(this) == DialogResult.OK)
            mediaPath = mediaDialog.FileName;

        try
        {
            UseWaitCursor = true;
            _status.Text = Localization.IsSerbian ? "Otvaram editor titlova…" : "Opening subtitle editor…";
            await Task.Yield();
            var cues = await SubtitleParser.LoadAsync(srtDialog.FileName, CancellationToken.None);
            if (cues.Count == 0)
                throw new InvalidDataException("SRT nema upotrebljive titlove.");

            var signals = cues.Select((_, index) => new ReviewSignal
            {
                CueNumber = index + 1,
                OpeningPhraseStatus = "—"
            }).ToList();

            using var editor = new SubtitleEditorForm(cues, signals, srtDialog.FileName, mediaPath, BuildSpeechDetectionOptions());
            UseWaitCursor = false;
            _status.Text = Localization.T("Editor je otvoren.");
            editor.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.Status(ex.Message), Localization.IsSerbian ? "Otvaranje SRT-a" : "Opening SRT", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void RefreshModelList(string? preferredId = null)
    {
        string modelsDirectory = Path.Combine(AppContext.BaseDirectory, "runtime", "models");
        string? currentId = preferredId ?? (_modelBox.SelectedItem as WhisperModelOption)?.Id;

        var models = new List<WhisperModelOption>
        {
            new("tiny", "Tiny — brzo", "ggml-tiny.bin", true),
            new("base", "Base — standard", "ggml-base.bin", true)
        };

        foreach (var optional in WhisperModelCatalog.OptionalModels)
        {
            if (File.Exists(Path.Combine(modelsDirectory, optional.FileName)))
                models.Add(optional);
        }

        _modelBox.BeginUpdate();
        _modelBox.Items.Clear();
        foreach (var model in models)
            _modelBox.Items.Add(model);
        _modelBox.EndUpdate();

        int index = models.FindIndex(x => x.Id == currentId);
        _modelBox.SelectedIndex = index >= 0 ? index : 0;
    }


    private void OpenBatchManager()
    {
        var models = _modelBox.Items.Cast<WhisperModelOption>().ToList();
        string selectedLanguageCode = (_languageBox.SelectedItem as ProcessingLanguageOption)?.Code
            ?? ProcessingLanguages.CodeFromDisplay(_languageBox.Text);
        using var dialog = new BatchManagerForm(models, _modelBox.SelectedIndex, selectedLanguageCode);
        dialog.ShowDialog(this);
    }

    private void OpenAdditionalModels()
    {
        using var dialog = new ModelManagerForm(Path.Combine(AppContext.BaseDirectory, "runtime", "models"));
        dialog.ShowDialog(this);
        RefreshModelList();
    }

    private SpeechDetectionOptions? BuildSpeechDetectionOptions()
    {
        if (_modelBox.SelectedItem is not WhisperModelOption selectedModel)
            return null;

        string runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
        ProcessingLanguageOption languageOption = _languageBox.SelectedItem as ProcessingLanguageOption
            ?? ProcessingLanguages.ByCode(ProcessingLanguages.CodeFromDisplay(_languageBox.Text));

        return new SpeechDetectionOptions(
            Path.Combine(runtime, "bin", "ffmpeg.exe"),
            Path.Combine(runtime, "bin", "whisper-cli.exe"),
            Path.Combine(runtime, "models", selectedModel.FileName),
            languageOption.WhisperCode,
            languageOption.Code);
    }

    private void OpenEditor()
    {
        if (_editableCues is null || _reviewSignals is null)
        {
            MessageBox.Show(this, Localization.T("Prvo napravi poravnati SRT."), "SubtitleBoom",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var editor = new SubtitleEditorForm(_editableCues, _reviewSignals, _outputBox.Text, _mediaBox.Text, BuildSpeechDetectionOptions());
        editor.ShowDialog(this);
    }

    private void ValidateInputs()
    {
        if (!File.Exists(_mediaBox.Text))
            throw new FileNotFoundException("Izaberi postojeći video ili audio.");
        if (!File.Exists(_subtitleBox.Text))
            throw new FileNotFoundException("Izaberi postojeći TXT ili podržani format titla.");
        if (string.IsNullOrWhiteSpace(_outputBox.Text))
            throw new InvalidDataException("Izaberi izlazni format titla.");
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(message + "\n\n" + path);
    }

    private void SetBusy(bool busy)
    {
        _alignButton.Enabled = !busy;
        _transcribeButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        UseWaitCursor = busy;
    }

    private void Report(int percent, string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Report(percent, status));
            return;
        }
        _progress.Value = Math.Clamp(percent, 0, 100);
        string localizedStatus = Localization.Status(status);
        _status.Text = localizedStatus;
        Log(localizedStatus);
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {Localization.Status(message)}{Environment.NewLine}");
    }
}
