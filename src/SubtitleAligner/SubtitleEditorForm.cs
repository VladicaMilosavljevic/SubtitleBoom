using System.Globalization;
using System.Text.Json;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using SubtitleAligner.Models;
using SubtitleAligner.Services;

namespace SubtitleAligner;

public sealed class SubtitleEditorForm : Form
{
    private static readonly TimeSpan MinimumCueGap = TimeSpan.FromMilliseconds(80);
    private readonly List<SubtitleCue> _cues;
    private readonly List<ReviewSignal> _signals;
    private string _defaultOutputPath;
    private readonly string _mediaPath;
    private readonly SpeechDetectionOptions? _speechDetectionOptions;
    private readonly Button _detectSpeechButton = new() { Text = "DETEKTUJ GOVOR ZA UČITANI SRT", AutoSize = true };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToResizeColumns = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoGenerateColumns = false };
    private readonly TextBox _startBox = new() { Width = 135 };
    private readonly TextBox _endBox = new() { Width = 135 };
    private readonly TextBox _textBox = new() { Multiline = true, Height = 100, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly Label _speechStart = new() { AutoSize = true };
    private readonly Label _speechEnd = new() { AutoSize = true };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _playerTime = new() { AutoSize = true, Text = "00:00:00,000", Margin = new Padding(8, 8, 0, 0) };
    private readonly TextBox _goToTimeBox = new() { Width = 120, Text = "00:00:00,000" };
    private readonly Button _undoButton = new() { Text = "PONIŠTI", Enabled = false };
    private readonly Button _redoButton = new() { Text = "PONOVI", Enabled = false };
    private readonly Button _playPauseButton = new() { Text = "Pauza", AutoSize = true };
    private readonly CheckBox _autoEndCheckBox = new() { Text = "Kada promenim početak, zadrži trajanje i razmak od 80 ms do sledećeg titla", AutoSize = true, Checked = true };
    private readonly CheckBox _followPlaybackCheckBox = new() { Text = "Prati reprodukciju", AutoSize = true, Checked = true };
    private readonly VideoView _videoView = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly WaveformPreviewControl _waveform = new() { Dock = DockStyle.Fill, Margin = new Padding(10, 4, 10, 4) };
    private readonly CheckBox _waveformSnapCheckBox = new() { Text = "Snap na detektovani govor", AutoSize = true, Checked = true, Margin = new Padding(16, 2, 0, 0) };
    private readonly CheckBox _waveformPreventOverlapCheckBox = new() { Text = "Zadrži razmak od 80 ms između susednih titlova", AutoSize = true, Checked = true, Margin = new Padding(16, 2, 0, 0) };
    private readonly Panel _audioOverlayPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 24, 24), Visible = false };
    private readonly AudioPreviewControl _audioPreview = new();
    private readonly bool _isAudioOnly;
    private readonly System.Windows.Forms.Timer _playerTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new() { Interval = 15000 };
    private readonly Stack<EditorSnapshot> _undo = new();
    private readonly Stack<EditorSnapshot> _redo = new();
    private readonly HashSet<int> _modifiedRows = new();
    private readonly ToolStripStatusLabel _playerStatusLabel = new("Plejer: učitavanje…");
    private readonly ToolStripStatusLabel _modelStatusLabel = new("Model: —");
    private readonly ToolStripStatusLabel _cueStatusLabel = new("Titl: —");
    private readonly ToolTip _toolTips = new();
    private readonly ToolStripStatusLabel _saveStatusLabel = new("Sve sačuvano") { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private Font? _gridBoldFont;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _media;
    private int _selectedIndex = -1;
    private string _projectPath;
    private readonly string _previewSrtPath;
    private bool _dirty;
    private bool _playerInitialized;
    private bool _suppressSelectionSeek;
    private bool _suppressGridSelectionChanged;
    private int _lastFollowedIndex = -1;
    private long _restoredPlayerPositionMilliseconds;
    private long _pendingSeekMilliseconds = -1;
    private bool _previewRefreshPending;
    private readonly ComboBox _toleranceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private bool _suppressToleranceChanges;
    private readonly NumericUpDown _customStrongLimit = new() { DecimalPlaces = 2, Increment = 0.05M, Minimum = 0.00M, Maximum = 10.00M, Value = 0.45M, Width = 75 };
    private readonly NumericUpDown _customModerateLimit = new() { DecimalPlaces = 2, Increment = 0.05M, Minimum = 0.05M, Maximum = 20.00M, Value = 1.00M, Width = 75 };
    private readonly FlowLayoutPanel _customTolerancePanel = new()
    {
        AutoSize = true,
        WrapContents = false,
        Visible = false,
        Margin = new Padding(8, 0, 0, 0)
    };
    private string _toleranceProfile = "Standardna";
    private double _customStrongLimitSeconds = 0.45;
    private double _customModerateLimitSeconds = 1.00;
    private long _initialProcessingMilliseconds;
    private DateTime? _initialProcessedAtUtc;
    private bool _createdByBatch;
    private Dictionary<string, long> _processingPhaseMilliseconds = new();
    private DateTime _projectCreatedAtUtc = DateTime.UtcNow;
    private DateTime? _lastOpenedAtUtc;
    private long _lastLoadMilliseconds;
    private long _totalLoadMilliseconds;
    private int _openCount;
    private DateTime? _lastEditedAtUtc;
    private int _manualEditCount;
    private int _applyCount;
    private int _autoSaveCount;
    private int _subtitleAddCount;
    private int _subtitleDeleteCount;
    private readonly System.Diagnostics.Stopwatch _projectLoadStopwatch = System.Diagnostics.Stopwatch.StartNew();

    public SubtitleEditorForm(List<SubtitleCue> cues, IReadOnlyList<ReviewSignal> signals, string defaultOutputPath, string mediaPath, SpeechDetectionOptions? speechDetectionOptions = null)
    {
        _cues = cues;
        _signals = signals.ToList();
        _defaultOutputPath = defaultOutputPath;
        _mediaPath = mediaPath;
        _isAudioOnly = IsAudioFile(_mediaPath);
        _speechDetectionOptions = speechDetectionOptions;
        _projectPath = WorkspacePaths.GetProjectPath(_defaultOutputPath);
        string previewDir = Path.Combine(Path.GetTempPath(), "SubtitleBoom", "Preview");
        Directory.CreateDirectory(previewDir);
        _previewSrtPath = Path.Combine(previewDir, $"preview_{Guid.NewGuid():N}.srt");

        Text = "SubtitleBoom v1.0";
        KeyPreview = true;
        Width = 1320;
        Height = 820;
        MinimumSize = new Size(1050, 680);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);
        _gridBoldFont = new Font(Font, FontStyle.Bold);

        _toleranceBox.Items.AddRange(["Stroga", "Standardna", "Opuštena", "Prilagođena"]);
        _toleranceBox.SelectedItem = _toleranceProfile;

        BuildLayout();
        LoadRows();
        _toleranceBox.SelectedIndexChanged += (_, _) => ApplyToleranceProfile();
        _customStrongLimit.ValueChanged += (_, _) => ApplyCustomToleranceFromControls();
        _customModerateLimit.ValueChanged += (_, _) => ApplyCustomToleranceFromControls();

        _grid.SelectionChanged += (_, _) =>
        {
            if (!_suppressGridSelectionChanged) LoadSelection();
        };
        _waveform.PositionRequested += (_, position) => SeekWithoutPlaying(position);
        _waveform.CueEditPreview += (_, e) => PreviewWaveformEdit(e.Start, e.End);
        _waveform.CueEditCommitted += (_, e) => CommitWaveformEdit(e.Start, e.End);
        _waveform.NeighborCueRequested += (_, e) => ActivateWaveformNeighbor(e.Offset);
        _waveformSnapCheckBox.CheckedChanged += (_, _) => _waveform.SnapToDetectedSpeech = _waveformSnapCheckBox.Checked;
        _waveformPreventOverlapCheckBox.CheckedChanged += (_, _) => _waveform.PreventNeighborOverlap = _waveformPreventOverlapCheckBox.Checked;
        _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _cues.Count) return;
            _followPlaybackCheckBox.Checked = true;
            SeekWithoutPlaying(_cues[e.RowIndex].Start);
            _status.Text = Localization.Status($"Plejer je postavljen na titl #{e.RowIndex + 1}.");
        };
        _playerTimer.Tick += (_, _) => UpdatePlayerClock();
        _playerTimer.Start();
        _autoSaveTimer.Tick += (_, _) => AutoSaveProject();
        _autoSaveTimer.Start();
        FormClosed += (_, _) => { AutoSaveProject(force: true); DisposePlayer(); _gridBoldFont?.Dispose(); _gridBoldFont = null; };
        KeyDown += SubtitleEditorForm_KeyDown;
        _speechStart.Cursor = Cursors.Hand;
        _speechStart.DoubleClick += (_, _) =>
        {
            if (CurrentSignal()?.DetectedSpeechStart is TimeSpan speech) SeekWithoutPlaying(speech);
        };
        _speechEnd.Cursor = Cursors.Hand;
        _speechEnd.DoubleClick += (_, _) =>
        {
            if (CurrentSignal()?.DetectedSpeechEnd is TimeSpan speech) SeekWithoutPlaying(speech);
        };

        // VLC initialization and the first seek are delayed until the editor is visible.
        // Doing this work in the constructor can block the WinForms UI while an SRT is opened.
        Shown += async (_, _) =>
        {
            // Prvo prikaži editor. Obnova projekta i VLC više ne blokiraju otvaranje prozora.
            TryRestoreProject();
            LoadRows();
            if (_grid.Rows.Count > 0)
                SelectRow(Math.Clamp(_selectedIndex, 0, _grid.Rows.Count - 1));

            await InitializePlayerAsync();
        };
        Localization.Apply(this);
        LocalizeToleranceOptions();
    }

    private void BuildLayout()
    {
        var menu = HelpSystem.CreateMenu(this);
        var fileMenu = new ToolStripMenuItem("Datoteka");
        fileMenu.DropDownItems.Add("Sačuvaj / Izvezi kao…", null, async (_, _) => await SaveAsAsync());
        fileMenu.DropDownItems.Add("Izvezi paket za YouTube (SRT + VTT + SBV)…", null, async (_, _) => await ExportYouTubePackageAsync());
        menu.Items.Insert(0, fileMenu);
        MainMenuStrip = menu;

        // Koristimo eksplicitne redove za meni, sadržaj i statusnu traku.
        // Tako MenuStrip nikada ne može da prekrije zaglavlje DataGridView tabele,
        // bez obzira na DPI skaliranje ili promenu veličine prozora.
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 45, DataPropertyName = "Number" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Titl", Width = 105, MinimumWidth = 105, Resizable = DataGridViewTriState.True, DataPropertyName = "Start" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Govor", Width = 105, MinimumWidth = 105, Resizable = DataGridViewTriState.True, DataPropertyName = "Speech" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", Width = 95, DataPropertyName = "Status" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tekst", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DataPropertyName = "Text" });
        _grid.CellFormatting += Grid_CellFormatting;
        var gridMenu = new ContextMenuStrip();
        gridMenu.Items.Add("Dodaj red iznad", null, (_, _) => InsertSubtitleAbove());
        gridMenu.Items.Add("Dodaj red ispod", null, (_, _) => InsertSubtitleBelow());
        gridMenu.Items.Add(new ToolStripSeparator());
        gridMenu.Items.Add("Obriši izabrani red", null, (_, _) => DeleteSelectedSubtitle());
        _grid.ContextMenuStrip = gridMenu;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 610 };
        split.Panel1.Controls.Add(_grid);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        right.Controls.Add(BuildPlayerPanel(), 0, 0);
        right.Controls.Add(BuildWaveformPanel(), 0, 1);
        right.Controls.Add(BuildEditorPanel(), 0, 2);
        split.Panel2.Controls.Add(right);

        var statusStrip = new StatusStrip { SizingGrip = false };
        statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _playerStatusLabel,
            new ToolStripSeparator(),
            _modelStatusLabel,
            new ToolStripSeparator(),
            _cueStatusLabel,
            _saveStatusLabel
        });
        page.Controls.Add(menu, 0, 0);
        page.Controls.Add(split, 0, 1);
        page.Controls.Add(statusStrip, 0, 2);
        Controls.Add(page);

        _modelStatusLabel.Text = Localization.T("Model:") + " " + (_speechDetectionOptions is null ? "—" : Path.GetFileNameWithoutExtension(_speechDetectionOptions.ModelPath).Replace("ggml-", string.Empty));
    }

    private Control BuildPlayerPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 4), RowCount = 2, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var videoHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        videoHost.Controls.Add(_videoView);

        _audioOverlayPanel.Controls.Add(_audioPreview);
        _audioOverlayPanel.Visible = _isAudioOnly;
        videoHost.Controls.Add(_audioOverlayPanel);
        if (_isAudioOnly) _audioOverlayPanel.BringToFront();

        panel.Controls.Add(videoHost, 0, 0);

        var controls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var playSubtitle = new Button { Text = "Pusti titl", AutoSize = true };
        var playSpeech = new Button { Text = "Pusti govor", AutoSize = true };
        var playNew = new Button { Text = "Pusti novu poziciju", AutoSize = true };
        var back = new Button { Text = "−2 s", AutoSize = true };
        var forward = new Button { Text = "+2 s", AutoSize = true };
        var goToTime = new Button { Text = "Idi na vreme", AutoSize = true };
        var copyCurrent = new Button { Text = "Kopiraj trenutno vreme", AutoSize = true };

        playSubtitle.Click += (_, _) => PlaySubtitlePosition();
        playSpeech.Click += (_, _) => PlaySpeechPosition();
        playNew.Click += (_, _) => PlayNewPosition();
        _playPauseButton.Click += (_, _) => TogglePlayPause();
        back.Click += (_, _) => SeekRelative(-2000);
        forward.Click += (_, _) => SeekRelative(2000);
        goToTime.Click += (_, _) => GoToEnteredTime();
        copyCurrent.Click += (_, _) => CopyCurrentPlayerTime();
        _goToTimeBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            GoToEnteredTime();
        };

        controls.Controls.Add(playSubtitle);
        controls.Controls.Add(playSpeech);
        controls.Controls.Add(playNew);
        controls.Controls.Add(_playPauseButton);
        controls.Controls.Add(back);
        controls.Controls.Add(forward);
        controls.Controls.Add(new Label { Text = "Idi na:", AutoSize = true, Margin = new Padding(12, 8, 2, 0) });
        controls.Controls.Add(_goToTimeBox);
        controls.Controls.Add(goToTime);
        controls.Controls.Add(copyCurrent);
        controls.Controls.Add(_followPlaybackCheckBox);
        controls.Controls.Add(_playerTime);
        _toolTips.SetToolTip(_playPauseButton, Localization.T("Pusti / Pauza (Space)"));
        _toolTips.SetToolTip(playSubtitle, Localization.T("Pusti od početka titla"));
        _toolTips.SetToolTip(playSpeech, Localization.T("Pusti od detektovanog govora"));
        _toolTips.SetToolTip(goToTime, Localization.T("Idi na unetu vremensku oznaku"));
        panel.Controls.Add(controls, 0, 1);
        return panel;
    }


    private Control BuildWaveformPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(10, 2, 0, 0) };
        header.Controls.Add(new Label
        {
            Text = "Grafički prikaz zvuka i titla",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true
        });
        header.Controls.Add(_waveformSnapCheckBox);
        header.Controls.Add(_waveformPreventOverlapCheckBox);
        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(_waveform, 0, 1);
        _toolTips.SetToolTip(_waveform, Localization.T("Povuci levu/desnu ivicu za početak/kraj. Povuci sredinu za ceo titl. Shift privremeno isključuje snap."));
        _toolTips.SetToolTip(_waveformSnapCheckBox, Localization.T("Automatski zalepi ivice titla za detektovani početak ili kraj govora kada su dovoljno blizu."));
        _toolTips.SetToolTip(_waveformPreventOverlapCheckBox, Localization.T("Kada je uključeno, grafičko povlačenje zadržava najmanje 80 ms između susednih titlova."));
        return panel;
    }

    private Control BuildEditorPanel()
    {
        // Desni donji deo ponovo koristi zaseban vertikalni klizač, kao u stabilnoj verziji.
        // Raspored kontrola ostaje isti; kada sadržaj ne stane, korisnik može da ga
        // pomera klizačem ili točkićem miša i dođe do polja PRIMENI i ostalih komandi.
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            TabStop = true,
            BackColor = SystemColors.Control
        };
        scrollHost.MouseEnter += (_, _) => scrollHost.Focus();

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 8),
            ColumnCount = 1,
            RowCount = 4,
            AutoScroll = false
        };
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 4)
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        details.Controls.Add(new Label { Text = "Početak:", AutoSize = true, Margin = new Padding(0, 7, 5, 0) }, 0, 0);
        var startRow = TimeRow(string.Empty, _startBox, "Trenutno → početak", () => SetBoxFromCurrentPlayer(_startBox));
        details.Controls.Add(startRow, 1, 0);
        details.Controls.Add(new Label { Text = "Kraj:", AutoSize = true, Margin = new Padding(12, 7, 5, 0) }, 2, 0);
        var endRow = TimeRow(string.Empty, _endBox, "Trenutno → kraj", () => SetBoxFromCurrentPlayer(_endBox));
        details.Controls.Add(endRow, 3, 0);

        details.Controls.Add(new Label { Text = "Govor početak:", AutoSize = true, Margin = new Padding(0, 7, 5, 0) }, 0, 1);
        details.Controls.Add(_speechStart, 1, 1);
        details.Controls.Add(new Label { Text = "Govor kraj:", AutoSize = true, Margin = new Padding(12, 7, 5, 0) }, 2, 1);
        details.Controls.Add(_speechEnd, 3, 1);

        _customTolerancePanel.Controls.Add(new Label
        {
            Text = "POUZDANO ≤",
            AutoSize = true,
            Margin = new Padding(0, 7, 4, 0)
        });
        _customTolerancePanel.Controls.Add(_customStrongLimit);
        _customTolerancePanel.Controls.Add(new Label
        {
            Text = "s",
            AutoSize = true,
            Margin = new Padding(2, 7, 8, 0)
        });
        _customTolerancePanel.Controls.Add(new Label
        {
            Text = "PROVERITI ≤",
            AutoSize = true,
            Margin = new Padding(0, 7, 4, 0)
        });
        _customTolerancePanel.Controls.Add(_customModerateLimit);
        _customTolerancePanel.Controls.Add(new Label
        {
            Text = "s",
            AutoSize = true,
            Margin = new Padding(2, 7, 0, 0)
        });

        var optionRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
        optionRow.Controls.Add(_autoEndCheckBox);
        optionRow.Controls.Add(new Label { Text = "Tolerancija:", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
        optionRow.Controls.Add(_toleranceBox);
        optionRow.Controls.Add(_customTolerancePanel);
        details.SetColumnSpan(optionRow, 4);
        details.Controls.Add(optionRow, 0, 2);
        editor.Controls.Add(details, 0, 0);

        var textAndStatus = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 4) };
        textAndStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
        textAndStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        _textBox.Dock = DockStyle.Fill;
        _textBox.MinimumSize = new Size(0, 62);
        textAndStatus.Controls.Add(_textBox, 0, 0);
        var statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8, 2, 0, 0) };
        statusPanel.Controls.Add(new Label { Text = "Status", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) }, 0, 0);
        statusPanel.Controls.Add(_status, 0, 1);
        statusPanel.Controls.Add(new Label { Text = "Izmena se automatski čuva", AutoSize = true, ForeColor = SystemColors.GrayText }, 0, 2);
        textAndStatus.Controls.Add(statusPanel, 1, 0);
        editor.Controls.Add(textAndStatus, 0, 1);

        var primary = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        var apply = new Button { Text = "PRIMENI (Ctrl+Enter)", AutoSize = true, Height = 34 };
        var autoFix = new Button { Text = "AUTO POPRAVKA", AutoSize = true, Height = 34 };
        var previousProblem = new Button { Text = "← PROBLEM", AutoSize = true, Height = 34 };
        var nextProblem = new Button { Text = "PROBLEM → (F3)", AutoSize = true, Height = 34 };
        var wrapLines = new Button { Text = "PRELOMI REDOVE", AutoSize = true, Height = 34 };
        var split = new Button { Text = "PODELI", AutoSize = true, Height = 34 };
        var smartSplit = new Button { Text = "PAMETNO PODELI", AutoSize = true, Height = 34 };
        var mergeNext = new Button { Text = "SPOJI SA SLEDEĆIM", AutoSize = true, Height = 34 };
        apply.Click += (_, _) => ApplyEdit();
        autoFix.Click += (_, _) => AutoFixCurrent();
        previousProblem.Click += (_, _) => SelectPreviousProblem();
        nextProblem.Click += (_, _) => SelectNextProblem();
        wrapLines.Click += (_, _) => WrapCurrentSubtitle();
        split.Click += (_, _) => SplitCurrentSubtitleAtCursor();
        smartSplit.Click += (_, _) => SmartSplitCurrentSubtitle();
        mergeNext.Click += (_, _) => MergeCurrentWithNext();
        primary.Controls.Add(apply);
        primary.Controls.Add(autoFix);
        primary.Controls.Add(wrapLines);
        primary.Controls.Add(split);
        primary.Controls.Add(smartSplit);
        primary.Controls.Add(mergeNext);
        primary.Controls.Add(previousProblem);
        primary.Controls.Add(nextProblem);
        primary.Controls.Add(_undoButton);
        primary.Controls.Add(_redoButton);
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        editor.Controls.Add(primary, 0, 2);

        var secondary = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 0) };
        var addAbove = new Button { Text = "+ RED IZNAD", AutoSize = true };
        var addBelow = new Button { Text = "+ RED ISPOD", AutoSize = true };
        var deleteRow = new Button { Text = "OBRIŠI RED", AutoSize = true };
        var projectDashboard = new Button { Text = "PREGLED PROJEKTA (F2)", AutoSize = true };
        addAbove.Click += (_, _) => InsertSubtitleAbove();
        addBelow.Click += (_, _) => InsertSubtitleBelow();
        deleteRow.Click += (_, _) => DeleteSelectedSubtitle();
        projectDashboard.Click += (_, _) => ShowProjectDashboard();
        _detectSpeechButton.Click += async (_, _) => await DetectSpeechForLoadedSrtAsync();
        secondary.Controls.Add(addAbove);
        secondary.Controls.Add(addBelow);
        secondary.Controls.Add(deleteRow);
        secondary.Controls.Add(_detectSpeechButton);
        secondary.Controls.Add(projectDashboard);
        editor.Controls.Add(secondary, 0, 3);

        _toolTips.SetToolTip(apply, Localization.T("Primeni izmenu (Ctrl+Enter)"));
        _toolTips.SetToolTip(autoFix, Localization.T("Automatski popravi trenutni titl kada je predlog pouzdan"));
        _toolTips.SetToolTip(wrapLines, Localization.T("Rasporedi tekst u najviše dva reda, do 44 znaka po redu"));
        _toolTips.SetToolTip(split, Localization.T("Podeli titl na mestu kursora u tekstu"));
        _toolTips.SetToolTip(smartSplit, Localization.T("Podeli titl na prirodnom mestu u tekstu"));
        _toolTips.SetToolTip(mergeNext, Localization.T("Spoji izabrani titl sa sledećim titlom"));
        _toolTips.SetToolTip(previousProblem, Localization.T("Prethodni označeni problem (Shift+F3)"));
        _toolTips.SetToolTip(nextProblem, Localization.T("Sledeći označeni problem (F3)"));
        _toolTips.SetToolTip(addAbove, Localization.T("Dodaj novi titl iznad izabranog reda"));
        _toolTips.SetToolTip(addBelow, Localization.T("Dodaj novi titl ispod izabranog reda"));
        _toolTips.SetToolTip(deleteRow, Localization.T("Obriši izabrani titl (Delete)"));
        _toolTips.SetToolTip(projectDashboard, Localization.T("Pregled stanja projekta (F2)"));
        _toolTips.SetToolTip(_undoButton, Localization.T("Poništi (Ctrl+Z)"));
        _toolTips.SetToolTip(_redoButton, Localization.T("Ponovi (Ctrl+Y)"));

        scrollHost.Controls.Add(editor);
        return scrollHost;
    }

    private static Control TimeRow(string label, TextBox box, string actionText, Action action)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        panel.Controls.Add(new Label { Text = label, Width = 120, Margin = new Padding(0, 7, 4, 0) });
        panel.Controls.Add(box);
        var button = new Button { Text = actionText, AutoSize = true };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
        return panel;
    }

    private async Task DetectSpeechForLoadedSrtAsync()
    {
        if (_speechDetectionOptions is null)
        {
            Warn("Nije izabran Whisper model. Zatvori editor, izaberi Tiny ili Base na glavnom ekranu i ponovo otvori SRT.");
            return;
        }
        if (!File.Exists(_mediaPath))
        {
            Warn("Za detekciju govora mora biti izabran video ili audio fajl.");
            return;
        }
        if (!File.Exists(_speechDetectionOptions.FfmpegPath) ||
            !File.Exists(_speechDetectionOptions.WhisperPath) ||
            !File.Exists(_speechDetectionOptions.ModelPath))
        {
            Warn("Nedostaje FFmpeg, whisper.cpp ili izabrani Whisper model.");
            return;
        }

        var answer = MessageBox.Show(this,
            Localization.IsSerbian
                ? "Program će analizirati govor i popuniti kolone Govor i Status.\n\nPostojeća vremena i tekst titlova neće biti promenjeni."
                : "The program will analyze speech and fill the Speech and Status columns.\n\nExisting subtitle times and text will not be changed.",
            Localization.IsSerbian ? "Detekcija govora" : "Speech detection", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (answer != DialogResult.OK) return;

        string work = Path.Combine(Path.GetTempPath(), "SubtitleBoom", "SpeechDetection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string wav = Path.Combine(work, "audio.wav");
        string jsonBase = Path.Combine(work, "recognition");

        try
        {
            _detectSpeechButton.Enabled = false;
            UseWaitCursor = true;
            _status.Text = Localization.Status("Izdvajam zvuk…");
            await new MediaService(_speechDetectionOptions.FfmpegPath)
                .PrepareAudioAsync(_mediaPath, wav, message => BeginInvoke(new Action(() => _status.Text = Localization.Status(message))), CancellationToken.None);

            _status.Text = Localization.Status("Tiny/Base prepoznaje govor…");
            var words = await new WhisperService(_speechDetectionOptions.WhisperPath).RecognizeAsync(
                wav,
                _speechDetectionOptions.ModelPath,
                _speechDetectionOptions.Language,
                jsonBase,
                message => BeginInvoke(new Action(() => _status.Text = Localization.Status(message))),
                CancellationToken.None);

            // AlignmentService menja vremena cue-ova, zato radimo nad kopijama i uzimamo samo govorne rezultate.
            var copies = _cues.Select(c => new SubtitleCue
            {
                Start = c.Start,
                End = c.End,
                Text = c.Text
            }).ToList();
            List<ReviewSignal> detected = new AlignmentService().Align(
                copies, words, _speechDetectionOptions.AlignmentLanguage ?? _speechDetectionOptions.Language);

            _signals.Clear();
            _signals.AddRange(detected);
            int selected = Math.Max(0, _selectedIndex);
            LoadRows();
            SelectRow(Math.Min(selected, Math.Max(0, _grid.Rows.Count - 1)));
            _dirty = true;
            AutoSaveProject(force: true);
            _status.Text = Localization.Status("Detekcija govora je završena i sačuvana u projektu.");
            MessageBox.Show(this,
                Localization.IsSerbian
                    ? "Polja Govor i Status su popunjena. Vremena i tekst titlova nisu menjani."
                    : "The Speech and Status fields have been filled. Subtitle times and text were not changed.",
                Localization.IsSerbian ? "Detekcija završena" : "Detection complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn("Detekcija govora nije uspela: " + ex.Message);
            _status.Text = Localization.Status("Detekcija govora nije uspela.");
        }
        finally
        {
            _detectSpeechButton.Enabled = true;
            UseWaitCursor = false;
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private async Task InitializePlayerAsync()
    {
        if (!File.Exists(_mediaPath))
        {
            _status.Text = Localization.Status("Media fajl nije pronađen; uređivanje titlova je i dalje dostupno.");
            _playerStatusLabel.Text = "Plejer: media fajl nije pronađen";
            return;
        }

        _status.Text = Localization.Status("Editor je otvoren. Plejer se učitava u pozadini…");
        _playerStatusLabel.Text = "Plejer: učitavanje…";
        _playPauseButton.Enabled = false;

        try
        {
            var playerObjects = await Task.Run(() =>
            {
                Core.Initialize();
                var libVlc = new LibVLC("--no-video-title-show", "--quiet");
                var mediaPlayer = new MediaPlayer(libVlc);
                return (libVlc, mediaPlayer);
            });

            if (IsDisposed)
            {
                playerObjects.mediaPlayer.Dispose();
                playerObjects.libVlc.Dispose();
                return;
            }

            _libVlc = playerObjects.libVlc;
            _mediaPlayer = playerObjects.mediaPlayer;
            _videoView.MediaPlayer = _mediaPlayer;

            await RefreshPlayerSubtitlePreviewAsync(preservePosition: false);
            _playerInitialized = true;
            _playPauseButton.Enabled = true;
            _status.Text = Localization.T("Plejer je spreman.");
            _playerStatusLabel.Text = Localization.T("Plejer: spreman");

            if (_restoredPlayerPositionMilliseconds > 0 && _mediaPlayer is not null)
            {
                _pendingSeekMilliseconds = _restoredPlayerPositionMilliseconds;
            }
            else if (_selectedIndex >= 0)
            {
                ReviewSignal? signal = CurrentSignal();
                SeekWithoutPlaying(signal?.DetectedSpeechStart ?? _cues[_selectedIndex].Start);
            }
        }
        catch (Exception ex)
        {
            _playPauseButton.Enabled = false;
            _status.Text = Localization.Status("Editor radi, ali plejer nije mogao da se pokrene: " + ex.Message);
            _playerStatusLabel.Text = Localization.T("Plejer: greška");
        }
    }

    private void PlaySubtitlePosition()
    {
        if (_selectedIndex < 0) return;
        PlayAt(_cues[_selectedIndex].Start);
    }

    private void PlaySpeechPosition()
    {
        if (CurrentSignal()?.DetectedSpeechStart is not TimeSpan speech)
        {
            Warn("Detektovana pozicija govora nije pronađena.");
            return;
        }
        PlayAt(speech);
    }

    private void PlayNewPosition()
    {
        if (!TryParse(_startBox.Text, out TimeSpan start))
        {
            Warn("Novo početno vreme nije ispravno.");
            return;
        }
        PlayAt(start);
    }

    private void PlayAt(TimeSpan position)
    {
        if (_media is null || _mediaPlayer is null)
        {
            Warn("Media fajl nije dostupan.");
            return;
        }

        long target = Math.Max(0, (long)position.TotalMilliseconds);
        if (!_mediaPlayer.IsPlaying)
            _mediaPlayer.Play();
        _mediaPlayer.Time = target;
        _pendingSeekMilliseconds = -1;
        _playPauseButton.Text = Localization.T("Pauza");
    }

    private TimeSpan CurrentPlayerPosition()
    {
        long position = _mediaPlayer?.IsPlaying == true || _pendingSeekMilliseconds < 0
            ? Math.Max(0, _mediaPlayer?.Time ?? 0)
            : _pendingSeekMilliseconds;
        return TimeSpan.FromMilliseconds(position);
    }

    private void CopyCurrentPlayerTime()
    {
        string value = Format(CurrentPlayerPosition());
        try
        {
            Clipboard.SetText(value);
            _status.Text = Localization.Status("Trenutno vreme je kopirano: " + value);
        }
        catch (Exception ex)
        {
            Warn("Vreme nije moglo da se kopira: " + ex.Message);
        }
    }

    private void SetBoxFromCurrentPlayer(TextBox target)
    {
        string value = Format(CurrentPlayerPosition());
        target.Text = value;
        if (ReferenceEquals(target, _startBox))
            AdjustEndAfterStartChange(value);
        target.Focus();
        target.SelectAll();
        _status.Text = Localization.Status("Upisano trenutno vreme plejera: " + value);
    }

    private void GoToEnteredTime()
    {
        if (!TryParseFlexible(_goToTimeBox.Text, out TimeSpan position))
        {
            Warn("Vremenska oznaka nije ispravna. Koristi HH:mm:ss,fff ili HH:mm:ss.fff.");
            _goToTimeBox.SelectAll();
            _goToTimeBox.Focus();
            return;
        }

        if (_mediaPlayer is not null && _mediaPlayer.Length > 0 && position.TotalMilliseconds > _mediaPlayer.Length)
        {
            Warn("Uneto vreme je izvan trajanja videa.");
            return;
        }

        SeekWithoutPlaying(position);
        _goToTimeBox.Text = Format(position);
        _status.Text = Localization.Status("Plejer je postavljen na " + Format(position) + ".");
    }

    private void TogglePlayPause()
    {
        if (_media is null || _mediaPlayer is null) return;
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            _playPauseButton.Text = Localization.T("Pusti");
            if (_previewRefreshPending) QueuePlayerPreviewRefresh(preservePosition: true);
        }
        else
        {
            _mediaPlayer.Play();
            if (_pendingSeekMilliseconds >= 0)
            {
                _mediaPlayer.Time = _pendingSeekMilliseconds;
                _pendingSeekMilliseconds = -1;
            }
            _playPauseButton.Text = Localization.T("Pauza");
        }
    }

    private void SeekRelative(long milliseconds)
    {
        if (_media is null || _mediaPlayer is null) return;
        long current = _pendingSeekMilliseconds >= 0
            ? _pendingSeekMilliseconds
            : Math.Max(0, _mediaPlayer.Time);
        long target = Math.Max(0, current + milliseconds);
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Time = target;
            _pendingSeekMilliseconds = -1;
        }
        else
        {
            _pendingSeekMilliseconds = target;
        }
    }

    private void UpdatePlayerClock()
    {
        if (_mediaPlayer is null) return;
        TimeSpan position = CurrentPlayerPosition();
        _playerTime.Text = Format(position);
        UpdateAudioSubtitleOverlay(position);
        _waveform.SetPlayhead(position);
        FollowPlayback(position);
        _playPauseButton.Text = _mediaPlayer.IsPlaying ? Localization.T("Pauza") : Localization.T("Pusti");
    }

    private void FollowPlayback(TimeSpan position)
    {
        if (!_followPlaybackCheckBox.Checked || !_mediaPlayer!.IsPlaying) return;

        // Ne otimaj selekciju dok korisnik aktivno menja vreme ili tekst.
        if (_startBox.Focused || _endBox.Focused || _textBox.Focused || _goToTimeBox.Focused) return;

        int index = FindCueAt(position);
        if (index < 0 || index == _lastFollowedIndex) return;

        _lastFollowedIndex = index;
        _suppressSelectionSeek = true;
        try
        {
            SelectRow(index);
            EnsureRowVisible(index);
        }
        finally
        {
            _suppressSelectionSeek = false;
        }
    }

    private void UpdateAudioSubtitleOverlay(TimeSpan position)
    {
        if (!_isAudioOnly) return;

        int index = FindCueAt(position);
        _audioPreview.SubtitleText = index >= 0 && index < _cues.Count
            ? _cues[index].Text
            : string.Empty;
    }

    private static bool IsAudioFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".wma";
    }

    private int FindCueAt(TimeSpan position)
    {
        int low = 0;
        int high = _cues.Count - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            SubtitleCue cue = _cues[mid];
            if (position < cue.Start)
                high = mid - 1;
            else if (position >= cue.End)
                low = mid + 1;
            else
                return mid;
        }
        return -1;
    }

    private void EnsureRowVisible(int index)
    {
        if (index < 0 || index >= _grid.Rows.Count || _grid.DisplayedRowCount(false) <= 0) return;
        int visibleCount = Math.Max(1, _grid.DisplayedRowCount(false));
        int first = Math.Max(0, index - (visibleCount / 2));
        first = Math.Min(first, Math.Max(0, _grid.Rows.Count - visibleCount));
        try { _grid.FirstDisplayedScrollingRowIndex = first; } catch { }
    }

    private void LoadRows()
    {
        bool previousSuppression = _suppressGridSelectionChanged;
        _suppressGridSelectionChanged = true;
        try
        {
            _grid.DataSource = _cues.Select((cue, index) =>
            {
                ReviewSignal? signal = index < _signals.Count ? _signals[index] : null;
                return CreateRowItem(cue, signal, index);
            }).ToList();
        }
        finally
        {
            _suppressGridSelectionChanged = previousSuppression;
        }
    }

    private (int RowOffset, int HorizontalOffset) CaptureGridViewport()
    {
        int firstVisible = 0;
        try
        {
            if (_grid.FirstDisplayedScrollingRowIndex >= 0)
                firstVisible = _grid.FirstDisplayedScrollingRowIndex;
        }
        catch { }

        int currentIndex = _grid.CurrentRow?.Index ?? Math.Max(0, _selectedIndex);
        return (Math.Max(0, currentIndex - firstVisible), Math.Max(0, _grid.HorizontalScrollingOffset));
    }

    private void RestoreGridViewport((int RowOffset, int HorizontalOffset) viewport, int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _grid.Rows.Count) return;

        int visibleCount = Math.Max(1, _grid.DisplayedRowCount(false));
        int maximumFirst = Math.Max(0, _grid.Rows.Count - visibleCount);
        int desiredFirst = Math.Clamp(targetIndex - viewport.RowOffset, 0, maximumFirst);
        try { _grid.FirstDisplayedScrollingRowIndex = desiredFirst; } catch { }
        try { _grid.HorizontalScrollingOffset = viewport.HorizontalOffset; } catch { }
    }

    private void RefreshRow(int index)
    {
        if (index < 0 || index >= _cues.Count) return;
        var viewport = CaptureGridViewport();
        if (_grid.DataSource is not List<RowItem> rows || index >= rows.Count)
        {
            LoadRows();
            SelectRow(index);
            RestoreGridViewport(viewport, index);
            return;
        }

        SubtitleCue cue = _cues[index];
        ReviewSignal? signal = index < _signals.Count ? _signals[index] : null;
        rows[index] = CreateRowItem(cue, signal, index);
        bool previousSuppression = _suppressGridSelectionChanged;
        _suppressGridSelectionChanged = true;
        try
        {
            _grid.DataSource = null;
            _grid.DataSource = rows;
        }
        finally
        {
            _suppressGridSelectionChanged = previousSuppression;
        }
        SelectRow(index);
        RestoreGridViewport(viewport, index);
    }

    private RowItem CreateRowItem(SubtitleCue cue, ReviewSignal? signal, int index) => new()
    {
        Number = index + 1,
        Start = $"{Format(cue.Start)} → {Format(cue.End)}",
        Speech = signal?.DetectedSpeechStart is TimeSpan speechStart
            ? $"{Format(speechStart)} → {(signal.DetectedSpeechEnd is TimeSpan speechEnd ? Format(speechEnd) : "—")}"
            : "NIJE PRONAĐEN",
        Status = NormalizeStatus(signal?.OpeningPhraseStatus),
        Text = cue.Text.Replace(Environment.NewLine, " ")
    };

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string? propertyName = grid.Columns[e.ColumnIndex].DataPropertyName;
        DataGridViewCellStyle? cellStyle = e.CellStyle;
        if (cellStyle is null) return;

        if (!string.Equals(propertyName, "Status", StringComparison.Ordinal))
        {
            cellStyle.BackColor = grid.DefaultCellStyle.BackColor;
            cellStyle.ForeColor = grid.DefaultCellStyle.ForeColor;
            cellStyle.SelectionBackColor = grid.DefaultCellStyle.SelectionBackColor;
            cellStyle.SelectionForeColor = grid.DefaultCellStyle.SelectionForeColor;
            cellStyle.Font = grid.Font;
        }

        if (grid.FindForm() is SubtitleEditorForm form && grid.Rows[e.RowIndex].DataBoundItem is RowItem row && form._modifiedRows.Contains(row.Number - 1))
        {
            if (!string.Equals(propertyName, "Status", StringComparison.Ordinal))
            {
                // Veoma svetla nebeskoplava označava ranije ručno obrađen red.
                // SelectionBackColor ostaje standardna tamnoplava radi jasne razlike.
                cellStyle.BackColor = Color.FromArgb(220, 240, 255);
                if (string.Equals(propertyName, "Number", StringComparison.Ordinal))
                    cellStyle.Font = _gridBoldFont;
            }
        }
        if (string.Equals(propertyName, "Number", StringComparison.Ordinal) &&
            grid.FindForm() is SubtitleEditorForm owner && grid.Rows[e.RowIndex].DataBoundItem is RowItem numberedRow &&
            owner._modifiedRows.Contains(numberedRow.Number - 1))
        {
            e.Value = "✎ " + numberedRow.Number.ToString(CultureInfo.InvariantCulture);
            e.FormattingApplied = true;
        }

        if (string.Equals(propertyName, "Speech", StringComparison.Ordinal) &&
            string.Equals(Convert.ToString(e.Value, CultureInfo.InvariantCulture), "NIJE PRONAĐEN", StringComparison.Ordinal))
        {
            cellStyle.ForeColor = Color.FromArgb(200, 0, 0);
            cellStyle.SelectionForeColor = Color.White;
            cellStyle.Font = _gridBoldFont;
            e.Value = Localization.T("NIJE PRONAĐEN");
            e.FormattingApplied = true;
        }

        if (!string.Equals(propertyName, "Status", StringComparison.Ordinal)) return;

        string status = Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? "—";
        DataGridViewCellStyle? style = e.CellStyle;
        if (style is null) return;
        style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        style.Font = _gridBoldFont;

        switch (status)
        {
            case "POUZDANO":
                style.BackColor = Color.FromArgb(198, 239, 206);
                style.ForeColor = Color.FromArgb(0, 97, 0);
                style.SelectionBackColor = Color.FromArgb(142, 210, 153);
                style.SelectionForeColor = Color.FromArgb(0, 70, 0);
                break;
            case "PROVERITI":
                style.BackColor = Color.FromArgb(255, 235, 156);
                style.ForeColor = Color.FromArgb(156, 101, 0);
                style.SelectionBackColor = Color.FromArgb(237, 205, 91);
                style.SelectionForeColor = Color.FromArgb(100, 62, 0);
                break;
            case "SLABO":
                style.BackColor = Color.FromArgb(255, 199, 206);
                style.ForeColor = Color.FromArgb(156, 0, 6);
                style.SelectionBackColor = Color.FromArgb(231, 145, 155);
                style.SelectionForeColor = Color.FromArgb(110, 0, 4);
                break;
            default:
                style.BackColor = grid.DefaultCellStyle.BackColor;
                style.ForeColor = grid.DefaultCellStyle.ForeColor;
                style.SelectionBackColor = grid.DefaultCellStyle.SelectionBackColor;
                style.SelectionForeColor = grid.DefaultCellStyle.SelectionForeColor;
                break;
        }
        e.Value = Localization.T(status);
        e.FormattingApplied = true;
    }

    private static string NormalizeStatus(string? value) => value switch
    {
        "STRONG" => "POUZDANO",
        "MODERATE" => "PROVERITI",
        "WEAK" => "SLABO",
        _ => "—"
    };

    private void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _cues.Count) return;
        ReviewSignal? signal = e.RowIndex < _signals.Count ? _signals[e.RowIndex] : null;
        if (signal is null) return;

        string startOffset = signal.DetectedSpeechStart is TimeSpan speechStart
            ? $"{(speechStart - _cues[e.RowIndex].Start).TotalSeconds:+0.000;-0.000;0.000} s"
            : Localization.T("nije pronađen");
        string endOffset = signal.DetectedSpeechEnd is TimeSpan speechEnd
            ? $"{(speechEnd - _cues[e.RowIndex].End).TotalSeconds:+0.000;-0.000;0.000} s"
            : Localization.T("nije pronađen");
        string confidence = signal.OpeningPhraseConfidence > 0
            ? $"{signal.OpeningPhraseConfidence * 100:0}%"
            : "—";

        e.ToolTipText = $"{Localization.T("Status:")} {Localization.T(NormalizeStatus(signal.OpeningPhraseStatus))}\n{Localization.T("Početak:")} {startOffset}\n{Localization.T("Kraj:")} {endOffset}\n{Localization.T("Confidence:")} {confidence}";
    }

    private void LoadSelection()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RowItem row) return;
        _selectedIndex = row.Number - 1;
        SubtitleCue cue = _cues[_selectedIndex];
        ReviewSignal? signal = _selectedIndex < _signals.Count ? _signals[_selectedIndex] : null;
        _startBox.Text = Format(cue.Start);
        _endBox.Text = Format(cue.End);
        _textBox.Text = cue.Text;
        _speechStart.Text = Localization.T("Početak:") + " " + (signal?.DetectedSpeechStart is TimeSpan start ? Format(start) : Localization.T("NIJE PRONAĐEN"));
        _speechEnd.Text = Localization.T("Kraj:") + "       " + (signal?.DetectedSpeechEnd is TimeSpan end ? Format(end) : Localization.T("NIJE PRONAĐEN"));
        _waveform.SetDetectedSpeech(signal?.DetectedSpeechStart, signal?.DetectedSpeechEnd);
        _cueStatusLabel.Text = $"{Localization.T("Titl:")} {_selectedIndex + 1} / {_cues.Count}";
        if (_isAudioOnly && _mediaPlayer is not null)
            UpdateAudioSubtitleOverlay(TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time)));
        SubtitleCue? previousCue = _selectedIndex > 0 ? _cues[_selectedIndex - 1] : null;
        SubtitleCue? nextCue = _selectedIndex + 1 < _cues.Count ? _cues[_selectedIndex + 1] : null;
        _ = _waveform.LoadAsync(_mediaPath, cue, previousCue, nextCue, _selectedIndex + 1);

        TimeSpan seek = signal?.DetectedSpeechStart ?? cue.Start;
        if (_playerInitialized && !_suppressSelectionSeek) SeekWithoutPlaying(seek);
    }

    private void SeekWithoutPlaying(TimeSpan position)
    {
        if (_media is null || _mediaPlayer is null) return;
        long target = Math.Max(0, (long)position.TotalMilliseconds);
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Time = target;
            _pendingSeekMilliseconds = -1;
        }
        else
        {
            _pendingSeekMilliseconds = target;
        }
    }

    private void ActivateWaveformNeighbor(int offset)
    {
        if ((offset != -1 && offset != 1) || _selectedIndex < 0) return;
        int target = _selectedIndex + offset;
        if (target < 0 || target >= _cues.Count) return;
        SelectRow(target);
        _status.Text = offset < 0
            ? (Localization.IsSerbian
                ? $"Prethodni titl #{target + 1} je izabran za grafičko uređivanje."
                : $"Previous subtitle #{target + 1} was selected for waveform editing.")
            : (Localization.IsSerbian
                ? $"Sledeći titl #{target + 1} je izabran za grafičko uređivanje."
                : $"Next subtitle #{target + 1} was selected for waveform editing.");
    }


    private void PreviewWaveformEdit(TimeSpan start, TimeSpan end)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _cues.Count) return;
        _startBox.Text = Format(start);
        _endBox.Text = Format(end);
        _status.Text = $"{(Localization.IsSerbian ? "Grafička izmena:" : "Waveform edit:")} {Format(start)} → {Format(end)}";
    }

    private void CommitWaveformEdit(TimeSpan start, TimeSpan end)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _cues.Count) return;
        SubtitleCue cue = _cues[_selectedIndex];
        if (start < TimeSpan.Zero || end <= start)
        {
            _waveform.UpdateCue(cue.Start, cue.End);
            _startBox.Text = Format(cue.Start);
            _endBox.Text = Format(cue.End);
            Warn("Grafički izabrano vreme nije ispravno.");
            return;
        }

        string? warning = BuildTimingWarning(_selectedIndex, start, end);
        if (warning is not null)
        {
            var answer = MessageBox.Show(this, warning + (Localization.IsSerbian
                ? "\n\nDa li ipak želiš da primeniš grafičku izmenu?"
                : "\n\nDo you still want to apply the waveform edit?"), Localization.IsSerbian ? "Upozorenje" : "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                _waveform.UpdateCue(cue.Start, cue.End);
                _startBox.Text = Format(cue.Start);
                _endBox.Text = Format(cue.End);
                return;
            }
        }

        if (cue.Start == start && cue.End == end) return;
        _undo.Push(Snapshot());
        _redo.Clear();
        cue.Start = start;
        cue.End = end;
        _modifiedRows.Add(_selectedIndex);
        _manualEditCount++;
        _applyCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        _saveStatusLabel.Text = Localization.T("Izmene nisu sačuvane u SRT");
        RefreshRow(_selectedIndex);
        QueuePlayerPreviewRefresh(preservePosition: true);
        UpdateHistoryButtons();
        _dirty = true;
        AutoSaveProject();
        _status.Text = Localization.IsSerbian
            ? "Grafička izmena vremena je primenjena i projekat je automatski sačuvan."
            : "The waveform timing edit has been applied and the project was saved automatically.";
    }

    private void ApplyEdit()
    {
        if (_selectedIndex < 0) return;
        if (!TryParse(_startBox.Text, out TimeSpan start) || !TryParse(_endBox.Text, out TimeSpan end))
        {
            Warn("Vreme mora biti u formatu HH:mm:ss,fff.");
            return;
        }
        SubtitleCue currentCue = _cues[_selectedIndex];
        if (_autoEndCheckBox.Checked && start != currentCue.Start)
        {
            end = CalculateAutomaticEnd(_selectedIndex, start, currentCue.End - currentCue.Start);
            _endBox.Text = Format(end);
        }
        if (start < TimeSpan.Zero || end <= start)
        {
            Warn("Kraj mora biti posle početka, a vreme ne može biti negativno.");
            return;
        }
        string text = _textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Warn("Tekst titla ne može biti prazan.");
            return;
        }

        string? warning = BuildTimingWarning(_selectedIndex, start, end);
        if (warning is not null)
        {
            var answer = MessageBox.Show(this, warning + (Localization.IsSerbian
                ? "\n\nDa li ipak želiš da primeniš izmenu?"
                : "\n\nDo you still want to apply the change?"), Localization.IsSerbian ? "Upozorenje" : "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        _undo.Push(Snapshot());
        _redo.Clear();
        SubtitleCue cue = _cues[_selectedIndex];
        cue.Start = start;
        cue.End = end;
        cue.Text = text;
        _modifiedRows.Add(_selectedIndex);
        _manualEditCount++;
        _applyCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        _saveStatusLabel.Text = Localization.T("Izmene nisu sačuvane u SRT");
        RefreshRow(_selectedIndex);
        QueuePlayerPreviewRefresh(preservePosition: true);
        UpdateHistoryButtons();
        _dirty = true;
        AutoSaveProject();
        _status.Text = Localization.Status("Izmena je primenjena i projekat je automatski sačuvan.");
    }

    private TimeSpan CalculateAutomaticEnd(int index, TimeSpan newStart, TimeSpan originalDuration)
    {
        // Normalno zadrži prethodno trajanje titla.
        TimeSpan candidate = newStart + originalDuration;

        // Samo kada je sledeći titl dovoljno blizu, ograniči kraj da ne dođe do preklapanja.
        if (index + 1 < _cues.Count)
        {
            TimeSpan latestAllowedEnd = _cues[index + 1].Start - MinimumCueGap;
            if (candidate > latestAllowedEnd)
                candidate = latestAllowedEnd;
        }

        return candidate;
    }

    private void AdjustEndAfterStartChange(string startText)
    {
        if (!_autoEndCheckBox.Checked || _selectedIndex < 0) return;
        if (!TryParse(startText, out TimeSpan start)) return;

        SubtitleCue cue = _cues[_selectedIndex];
        TimeSpan duration = cue.End - cue.Start;
        TimeSpan end = CalculateAutomaticEnd(_selectedIndex, start, duration);
        if (end > start) _endBox.Text = Format(end);
    }

    private async Task RefreshPlayerSubtitlePreviewAsync(bool preservePosition)
    {
        if (!File.Exists(_mediaPath) || _libVlc is null || _mediaPlayer is null) return;

        long position = preservePosition ? Math.Max(0, (long)CurrentPlayerPosition().TotalMilliseconds) : 0;
        try
        {
            await SrtWriter.SaveAsync(_previewSrtPath, _cues, CancellationToken.None);

            _mediaPlayer.Stop();
            _media?.Dispose();
            _media = new Media(_libVlc, _mediaPath, FromType.FromPath);
            _media.AddOption($":sub-file={_previewSrtPath}");
            _media.AddOption(":sub-autodetect-file=0");
            _mediaPlayer.Media = _media;
            _pendingSeekMilliseconds = position;
            _playPauseButton.Text = Localization.T("Pusti");
        }
        catch (Exception ex)
        {
            _status.Text = Localization.Status("Preview titla nije mogao odmah da se osveži: " + ex.Message);
        }
    }

    private async void QueuePlayerPreviewRefresh(bool preservePosition)
    {
        if (!_playerInitialized) return;
        if (_mediaPlayer?.IsPlaying == true)
        {
            _previewRefreshPending = true;
            return;
        }
        _previewRefreshPending = false;
        await RefreshPlayerSubtitlePreviewAsync(preservePosition);
    }

    private void AutoFixCurrent()
    {
        if (_selectedIndex < 0) return;
        ReviewSignal? signal = CurrentSignal();
        if (signal?.DetectedSpeechStart is not TimeSpan speechStart)
        {
            Warn("Auto Fix nije moguć jer detektovani govor nije pronađen.");
            return;
        }

        SubtitleCue cue = _cues[_selectedIndex];
        TimeSpan duration = cue.End - cue.Start;
        TimeSpan newEnd = _autoEndCheckBox.Checked
            ? CalculateAutomaticEnd(_selectedIndex, speechStart, duration)
            : speechStart + duration;
        double delta = (speechStart - cue.Start).TotalSeconds;
        _startBox.Text = Format(speechStart);
        _endBox.Text = Format(newEnd);

        var answer = MessageBox.Show(this,
            Localization.IsSerbian
                ? $"Predloženo pomeranje: {delta:+0.000;-0.000;0.000} s\n\nPre:   {Format(cue.Start)} --> {Format(cue.End)}\nPosle: {Format(speechStart)} --> {Format(newEnd)}\n\nPrimeni Auto Fix?"
                : $"Suggested shift: {delta:+0.000;-0.000;0.000} s\n\nBefore: {Format(cue.Start)} --> {Format(cue.End)}\nAfter:  {Format(speechStart)} --> {Format(newEnd)}\n\nApply Auto Fix?",
            "Auto Fix", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) ApplyEdit();
    }

    private void WrapCurrentSubtitle()
    {
        if (_selectedIndex < 0) return;
        string text = NormalizeSubtitleText(_textBox.Text);
        if (text.Length == 0) { Warn("Titl nema tekst za prelom."); return; }

        string? wrapped = BuildTwoLineText(text, 44);
        if (wrapped is null)
        {
            Warn("Tekst ne može uredno da stane u dva reda od najviše 44 znaka. Upotrebi PODELI ili PAMETNO PODELI.");
            return;
        }
        if (wrapped == _cues[_selectedIndex].Text) { _status.Text = Localization.Status("Titl već ima odgovarajući prelom redova."); return; }

        _undo.Push(Snapshot());
        _redo.Clear();
        _cues[_selectedIndex].Text = wrapped;
        MarkFormattingEdit(_selectedIndex, "Redovi su prelomljeni i projekat je automatski sačuvan.");
    }

    private void SplitCurrentSubtitleAtCursor()
    {
        if (_selectedIndex < 0) return;
        string raw = _textBox.Text;
        int cursor = _textBox.SelectionStart;
        if (cursor <= 0 || cursor >= raw.Length)
        {
            Warn("Postavi kursor između dve reči na mestu na kojem želiš da podeliš titl.");
            return;
        }

        string first = NormalizeSubtitleText(raw[..cursor]);
        string second = NormalizeSubtitleText(raw[cursor..]);
        if (first.Length == 0 || second.Length == 0)
        {
            Warn("Podela mora da ostavi tekst sa obe strane kursora.");
            return;
        }
        SplitCurrentSubtitle(first, second);
    }

    private void SmartSplitCurrentSubtitle()
    {
        if (_selectedIndex < 0) return;
        string text = NormalizeSubtitleText(_textBox.Text);
        int splitAt = FindNaturalSplit(text);
        if (splitAt <= 0 || splitAt >= text.Length)
        {
            Warn("Nije pronađeno bezbedno mesto za podelu ovog titla.");
            return;
        }
        SplitCurrentSubtitle(text[..splitAt].Trim(), text[splitAt..].Trim());
    }

    private void SplitCurrentSubtitle(string firstText, string secondText)
    {
        int index = _selectedIndex;
        SubtitleCue original = _cues[index];
        TimeSpan duration = original.End - original.Start;
        TimeSpan minimum = TimeSpan.FromMilliseconds(500);
        if (duration < minimum + minimum + MinimumCueGap)
        {
            Warn(Localization.IsSerbian
                ? "Titl nema dovoljno trajanja za dva dela od najmanje 0,5 sekundi i razmak od 80 ms."
                : "The subtitle is not long enough for two parts of at least 0.5 seconds with an 80 ms gap.");
            return;
        }

        double ratio = (double)firstText.Length / Math.Max(1, firstText.Length + secondText.Length);
        ratio = Math.Clamp(ratio, 0.2, 0.8);
        TimeSpan usableDuration = duration - MinimumCueGap;
        TimeSpan firstDuration = TimeSpan.FromTicks((long)(usableDuration.Ticks * ratio));
        if (firstDuration < minimum) firstDuration = minimum;
        if (usableDuration - firstDuration < minimum) firstDuration = usableDuration - minimum;
        TimeSpan firstEnd = original.Start + firstDuration;
        TimeSpan secondStart = firstEnd + MinimumCueGap;

        _undo.Push(Snapshot());
        _redo.Clear();
        EnsureSignalCountMatchesCues();
        ReviewSignal oldSignal = _signals[index];
        _cues[index] = new SubtitleCue { Start = original.Start, End = firstEnd, Text = firstText };
        _cues.Insert(index + 1, new SubtitleCue { Start = secondStart, End = original.End, Text = secondText });
        _signals[index] = FormattingSignal(index + 1, oldSignal.DetectedSpeechStart, firstEnd);
        _signals.Insert(index + 1, FormattingSignal(index + 2, secondStart, oldSignal.DetectedSpeechEnd));
        ShiftModifiedRowsForInsert(index + 1);
        _modifiedRows.Add(index);
        _modifiedRows.Add(index + 1);
        _subtitleAddCount++;
        _manualEditCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        RenumberSignals();
        FinishStructuralEdit("Titl je podeljen na dva reda i projekat je automatski sačuvan.");
    }

    private void MergeCurrentWithNext()
    {
        int index = _selectedIndex;
        if (index < 0 || index + 1 >= _cues.Count)
        {
            Warn("Izabrani titl nema sledeći titl sa kojim može da se spoji.");
            return;
        }

        SubtitleCue first = _cues[index];
        SubtitleCue second = _cues[index + 1];
        TimeSpan gap = second.Start - first.End;
        string mergedPlain = NormalizeSubtitleText(first.Text + " " + second.Text);
        string mergedText = BuildTwoLineText(mergedPlain, 44) ?? mergedPlain;
        var warnings = new List<string>();
        if (gap > TimeSpan.FromSeconds(2)) warnings.Add(Localization.IsSerbian ? $"Razmak između titlova je {gap.TotalSeconds:0.00} s." : $"The gap between subtitles is {gap.TotalSeconds:0.00} s.");
        if (mergedPlain.Length > 88) warnings.Add(Localization.IsSerbian ? "Spojeni tekst je duži od dva reda po 44 znaka." : "The merged text is longer than two lines of 44 characters.");
        double cps = mergedPlain.Length / Math.Max(0.1, (second.End - first.Start).TotalSeconds);
        if (cps > 20) warnings.Add(Localization.IsSerbian ? $"Brzina čitanja bi bila približno {cps:0.0} znakova u sekundi." : $"The reading speed would be approximately {cps:0.0} characters per second.");
        if (warnings.Count > 0)
        {
            string message = string.Join(Environment.NewLine, warnings) + (Localization.IsSerbian ? "\n\nDa li ipak želiš da spojiš titlove?" : "\n\nDo you still want to merge the subtitles?");
            if (MessageBox.Show(this, message, Localization.IsSerbian ? "Provera spajanja" : "Merge check", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        _undo.Push(Snapshot());
        _redo.Clear();
        EnsureSignalCountMatchesCues();
        ReviewSignal firstSignal = _signals[index];
        ReviewSignal secondSignal = _signals[index + 1];
        _cues[index] = new SubtitleCue { Start = first.Start, End = second.End, Text = mergedText };
        _cues.RemoveAt(index + 1);
        _signals[index] = FormattingSignal(index + 1, firstSignal.DetectedSpeechStart, secondSignal.DetectedSpeechEnd);
        _signals.RemoveAt(index + 1);
        ShiftModifiedRowsForDelete(index + 1);
        _modifiedRows.Add(index);
        _subtitleDeleteCount++;
        _manualEditCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        RenumberSignals();
        FinishStructuralEdit("Titl je spojen sa sledećim i projekat je automatski sačuvan.");
    }

    private void MarkFormattingEdit(int index, string message)
    {
        _modifiedRows.Add(index);
        _manualEditCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        FinishStructuralEdit(message);
    }

    private static ReviewSignal FormattingSignal(int cueNumber, TimeSpan? speechStart, TimeSpan? speechEnd) => new()
    {
        CueNumber = cueNumber,
        DetectedSpeechStart = speechStart,
        DetectedSpeechEnd = speechEnd,
        OpeningPhraseStatus = "WEAK"
    };

    private static string NormalizeSubtitleText(string text)
        => string.Join(" ", text.Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? BuildTwoLineText(string text, int maxLineLength)
    {
        text = NormalizeSubtitleText(text);
        if (text.Length <= maxLineLength) return text;
        int best = -1;
        int bestScore = int.MaxValue;
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] != ' ') continue;
            int left = i;
            int right = text.Length - i - 1;
            if (left > maxLineLength || right > maxLineLength) continue;
            int score = Math.Abs(left - right);
            if (score < bestScore) { best = i; bestScore = score; }
        }
        return best < 0 ? null : text[..best].TrimEnd() + Environment.NewLine + text[(best + 1)..].TrimStart();
    }

    private static int FindNaturalSplit(string text)
    {
        text = NormalizeSubtitleText(text);
        if (text.Length < 3) return -1;
        int target = text.Length / 2;
        int best = -1;
        int bestScore = int.MinValue;
        for (int i = 1; i < text.Length - 1; i++)
        {
            if (text[i] != ' ') continue;
            char previous = text[i - 1];
            int punctuationBonus = previous is '.' or '!' or '?' ? 100 : previous is ',' or ';' or ':' ? 45 : 0;
            int score = punctuationBonus - Math.Abs(i - target);
            if (i < text.Length * 0.2 || i > text.Length * 0.8) score -= 50;
            if (score > bestScore) { best = i; bestScore = score; }
        }
        return best;
    }

    private void SelectNextProblem()
    {
        if (_grid.Rows.Count == 0) return;
        int start = Math.Max(-1, _selectedIndex);
        for (int offset = 1; offset <= _cues.Count; offset++)
        {
            int index = (start + offset) % _cues.Count;
            ReviewSignal? signal = index < _signals.Count ? _signals[index] : null;
            if (IsProblem(signal))
            {
                SelectRow(index);
                TimeSpan seek = signal?.DetectedSpeechStart ?? _cues[index].Start;
                SeekWithoutPlaying(seek > TimeSpan.FromSeconds(2) ? seek - TimeSpan.FromSeconds(2) : TimeSpan.Zero);
                _status.Text = Localization.IsSerbian
                    ? $"Sledeći problem: titl #{index + 1}."
                    : $"Next problem: subtitle #{index + 1}.";
                return;
            }
        }
        _status.Text = Localization.IsSerbian
            ? "Nema drugih označenih problema."
            : "There are no other flagged problems.";
    }

    private static bool IsProblem(ReviewSignal? signal)
        => signal is not null && !string.Equals(signal.OpeningPhraseStatus, "STRONG", StringComparison.OrdinalIgnoreCase);

    private void SelectPreviousProblem()
    {
        if (_grid.Rows.Count == 0) return;
        int start = _selectedIndex < 0 ? 0 : _selectedIndex;
        for (int offset = 1; offset <= _cues.Count; offset++)
        {
            int index = (start - offset + _cues.Count) % _cues.Count;
            ReviewSignal? signal = index < _signals.Count ? _signals[index] : null;
            if (!IsProblem(signal)) continue;

            SelectRow(index);
            TimeSpan seek = signal?.DetectedSpeechStart ?? _cues[index].Start;
            SeekWithoutPlaying(seek > TimeSpan.FromSeconds(2) ? seek - TimeSpan.FromSeconds(2) : TimeSpan.Zero);
            _status.Text = Localization.IsSerbian
                ? $"Prethodni problem: titl #{index + 1}."
                : $"Previous problem: subtitle #{index + 1}.";
            return;
        }
        _status.Text = Localization.IsSerbian
            ? "Nema drugih označenih problema."
            : "There are no other flagged problems.";
    }


    private void ShowProjectDashboard()
    {
        ProjectDashboardForm.ShowDashboard(
            this,
            _mediaPath,
            _defaultOutputPath,
            _projectPath,
            _cues,
            _signals,
            _modifiedRows.Count,
            _dirty,
            _speechDetectionOptions?.AlignmentLanguage ?? _speechDetectionOptions?.Language ?? "—",
            _speechDetectionOptions is null
                ? "—"
                : Path.GetFileNameWithoutExtension(_speechDetectionOptions.ModelPath).Replace("ggml-", string.Empty),
            _initialProcessingMilliseconds,
            _initialProcessedAtUtc,
            _createdByBatch,
            _toleranceProfile == "Prilagođena"
                ? $"Prilagođena ({_customStrongLimitSeconds:0.00} / {_customModerateLimitSeconds:0.00} s)"
                : _toleranceProfile,
            _processingPhaseMilliseconds,
            _projectCreatedAtUtc,
            _lastOpenedAtUtc,
            _lastLoadMilliseconds,
            _totalLoadMilliseconds,
            _openCount,
            _lastEditedAtUtc,
            _manualEditCount,
            _applyCount,
            _autoSaveCount,
            _subtitleAddCount,
            _subtitleDeleteCount);
    }

    private async void SubtitleEditorForm_KeyDown(object? sender, KeyEventArgs e)
    {
        bool textInputFocused = _startBox.Focused || _endBox.Focused || _textBox.Focused || _goToTimeBox.Focused;

        if (e.KeyCode == Keys.F2)
        {
            e.SuppressKeyPress = true;
            ShowProjectDashboard();
            return;
        }
        if (e.KeyCode == Keys.F1)
        {
            e.SuppressKeyPress = true;
            HelpSystem.ShowShortcuts(this);
            return;
        }
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            await SaveAsAsync();
            return;
        }
        if (e.Control && e.KeyCode == Keys.Z)
        {
            e.SuppressKeyPress = true;
            Undo();
            return;
        }
        if (e.Control && e.KeyCode == Keys.Y)
        {
            e.SuppressKeyPress = true;
            Redo();
            return;
        }
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            ApplyEdit();
            return;
        }
        if (!textInputFocused && e.KeyCode == Keys.Delete)
        {
            e.SuppressKeyPress = true;
            DeleteSelectedSubtitle();
            return;
        }
        if (e.KeyCode == Keys.F3)
        {
            e.SuppressKeyPress = true;
            if (e.Shift) SelectPreviousProblem(); else SelectNextProblem();
            return;
        }
        if (e.Alt && e.KeyCode == Keys.Left)
        {
            e.SuppressKeyPress = true;
            SelectAdjacentSubtitle(-1);
            return;
        }
        if (e.Alt && e.KeyCode == Keys.Right)
        {
            e.SuppressKeyPress = true;
            SelectAdjacentSubtitle(1);
            return;
        }
        if (!textInputFocused && e.KeyCode == Keys.Home)
        {
            e.SuppressKeyPress = true;
            SelectSubtitleByIndex(0);
            return;
        }
        if (!textInputFocused && e.KeyCode == Keys.End)
        {
            e.SuppressKeyPress = true;
            SelectSubtitleByIndex(_cues.Count - 1);
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            StopPlayback();
            return;
        }
        if (e.KeyCode == Keys.Space && !textInputFocused)
        {
            e.SuppressKeyPress = true;
            TogglePlayPause();
        }
    }

    private void SelectAdjacentSubtitle(int direction)
    {
        if (_cues.Count == 0) return;
        int current = _selectedIndex < 0 ? 0 : _selectedIndex;
        SelectSubtitleByIndex(Math.Clamp(current + direction, 0, _cues.Count - 1));
    }

    private void SelectSubtitleByIndex(int index)
    {
        if (_cues.Count == 0) return;
        index = Math.Clamp(index, 0, _cues.Count - 1);
        SelectRow(index);
        SeekWithoutPlaying(_cues[index].Start);
        _status.Text = string.Format(Localization.T("Izabran je titl #{0}."), index + 1);
    }

    private void StopPlayback()
    {
        if (_mediaPlayer is null) return;
        _mediaPlayer.Pause();
        _playPauseButton.Text = Localization.T("Pusti");
        _playerStatusLabel.Text = Localization.T("Plejer: zaustavljen");
    }

    private void AutoSaveProject(bool force = false)
    {
        if (!force && !_dirty) return;
        try
        {
            var project = new ProjectState
            {
                Version = 8,
                ProjectAppVersion = "4.1.1",
                ProjectFormatVersion = 8,
                MediaPath = _mediaPath,
                SrtPath = _defaultOutputPath,
                SelectedIndex = _selectedIndex,
                SavedAtUtc = DateTime.UtcNow,
                PlayerPositionMilliseconds = Math.Max(0, _mediaPlayer?.Time ?? _restoredPlayerPositionMilliseconds),
                ModelFileName = _speechDetectionOptions is null ? null : Path.GetFileName(_speechDetectionOptions.ModelPath),
                Language = _speechDetectionOptions?.AlignmentLanguage ?? _speechDetectionOptions?.Language,
                ToleranceProfile = _toleranceProfile,
                CustomStrongLimitSeconds = _customStrongLimitSeconds,
                CustomModerateLimitSeconds = _customModerateLimitSeconds,
                InitialProcessingMilliseconds = _initialProcessingMilliseconds,
                InitialProcessedAtUtc = _initialProcessedAtUtc,
                CreatedByBatch = _createdByBatch,
                ProcessingPhaseMilliseconds = _processingPhaseMilliseconds,
                ProjectCreatedAtUtc = _projectCreatedAtUtc,
                LastOpenedAtUtc = _lastOpenedAtUtc,
                LastLoadMilliseconds = _lastLoadMilliseconds,
                TotalLoadMilliseconds = _totalLoadMilliseconds,
                OpenCount = _openCount,
                LastEditedAtUtc = _lastEditedAtUtc,
                ManualEditCount = _manualEditCount,
                ApplyCount = _applyCount,
                AutoSaveCount = _autoSaveCount + 1,
                SubtitleAddCount = _subtitleAddCount,
                SubtitleDeleteCount = _subtitleDeleteCount,
                ModifiedRows = _modifiedRows.OrderBy(index => index).ToList(),
                Cues = _cues.Select((c, index) =>
                {
                    ReviewSignal? signal = index < _signals.Count ? _signals[index] : null;
                    return new ProjectCue
                    {
                        StartMilliseconds = (long)c.Start.TotalMilliseconds,
                        EndMilliseconds = (long)c.End.TotalMilliseconds,
                        Text = c.Text,
                        SpeechStartMilliseconds = signal?.DetectedSpeechStart is TimeSpan speechStart ? (long)speechStart.TotalMilliseconds : null,
                        SpeechEndMilliseconds = signal?.DetectedSpeechEnd is TimeSpan speechEnd ? (long)speechEnd.TotalMilliseconds : null,
                        StartOffsetMilliseconds = signal?.DetectedSpeechStart is TimeSpan detectedStart ? (long)(detectedStart - c.Start).TotalMilliseconds : null,
                        EndOffsetMilliseconds = signal?.DetectedSpeechEnd is TimeSpan detectedEnd ? (long)(detectedEnd - c.End).TotalMilliseconds : null,
                        Confidence = signal?.OpeningPhraseConfidence ?? 0,
                        OpeningPhraseStatus = signal?.OpeningPhraseStatus
                    };
                }).ToList()
            };
            string? dir = Path.GetDirectoryName(_projectPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_projectPath, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
            _autoSaveCount++;
            _dirty = false;
            _saveStatusLabel.Text = Localization.T(_modifiedRows.Count > 0 ? "Projekat automatski sačuvan" : "Sve sačuvano");
        }
        catch (Exception ex)
        {
            _status.Text = Localization.Status("Auto Save nije uspeo: " + ex.Message);
        }
    }

    private void TryRestoreProject()
    {
        string projectPath = WorkspacePaths.ResolveAndMigrateFile(
            _projectPath,
            WorkspacePaths.GetLegacyProjectPath(_defaultOutputPath));
        if (!File.Exists(projectPath)) return;
        try
        {
            ProjectState? project = JsonSerializer.Deserialize<ProjectState>(File.ReadAllText(projectPath));
            if (project?.Cues is null || project.Cues.Count == 0) return;

            // Projekat je izvor istine i kada je korisnik dodavao ili brisao redove.
            // Zato ne zahtevamo da broj redova bude isti kao u prvobitnom SRT-u.
            _cues.Clear();
            foreach (ProjectCue savedCue in project.Cues)
            {
                _cues.Add(new SubtitleCue
                {
                    Start = TimeSpan.FromMilliseconds(savedCue.StartMilliseconds),
                    End = TimeSpan.FromMilliseconds(savedCue.EndMilliseconds),
                    Text = savedCue.Text ?? string.Empty
                });
            }
            bool projectContainsSpeechData = project.Version >= 2 || project.Cues.Any(c =>
                c.SpeechStartMilliseconds.HasValue || c.SpeechEndMilliseconds.HasValue || !string.IsNullOrWhiteSpace(c.OpeningPhraseStatus));
            if (projectContainsSpeechData)
            {
                _signals.Clear();
                for (int i = 0; i < project.Cues.Count; i++)
                {
                    ProjectCue saved = project.Cues[i];
                    _signals.Add(new ReviewSignal
                    {
                        CueNumber = i + 1,
                        DetectedSpeechStart = saved.SpeechStartMilliseconds is long speechStart ? TimeSpan.FromMilliseconds(speechStart) : null,
                        DetectedSpeechEnd = saved.SpeechEndMilliseconds is long speechEnd ? TimeSpan.FromMilliseconds(speechEnd) : null,
                        OpeningPhraseStatus = saved.OpeningPhraseStatus ?? "—"
                    });
                }
            }
            _selectedIndex = Math.Clamp(project.SelectedIndex, 0, Math.Max(0, _cues.Count - 1));
            _restoredPlayerPositionMilliseconds = Math.Max(0, project.PlayerPositionMilliseconds);
            _toleranceProfile = string.IsNullOrWhiteSpace(project.ToleranceProfile) ? "Standardna" : project.ToleranceProfile;
            _customStrongLimitSeconds = project.CustomStrongLimitSeconds > 0 ? project.CustomStrongLimitSeconds : 0.45;
            _customModerateLimitSeconds = project.CustomModerateLimitSeconds > _customStrongLimitSeconds ? project.CustomModerateLimitSeconds : 1.00;
            _customStrongLimit.Value = (decimal)Math.Clamp(_customStrongLimitSeconds, (double)_customStrongLimit.Minimum, (double)_customStrongLimit.Maximum);
            _customModerateLimit.Value = (decimal)Math.Clamp(_customModerateLimitSeconds, (double)_customModerateLimit.Minimum, (double)_customModerateLimit.Maximum);
            _initialProcessingMilliseconds = project.InitialProcessingMilliseconds;
            _initialProcessedAtUtc = project.InitialProcessedAtUtc;
            _createdByBatch = project.CreatedByBatch;
            _processingPhaseMilliseconds = project.ProcessingPhaseMilliseconds ?? new();
            _projectCreatedAtUtc = project.ProjectCreatedAtUtc == default ? (project.InitialProcessedAtUtc ?? DateTime.UtcNow) : project.ProjectCreatedAtUtc;
            _lastEditedAtUtc = project.LastEditedAtUtc;
            _manualEditCount = project.ManualEditCount;
            _applyCount = project.ApplyCount;
            _autoSaveCount = project.AutoSaveCount;
            _subtitleAddCount = project.SubtitleAddCount;
            _subtitleDeleteCount = project.SubtitleDeleteCount;
            _modifiedRows.Clear();
            foreach (int index in (project.ModifiedRows ?? new()).Where(index => index >= 0 && index < _cues.Count))
                _modifiedRows.Add(index);
            _projectLoadStopwatch.Stop();
            _lastLoadMilliseconds = Math.Max(1, _projectLoadStopwatch.ElapsedMilliseconds);
            _totalLoadMilliseconds = project.TotalLoadMilliseconds + _lastLoadMilliseconds;
            _openCount = project.OpenCount + 1;
            _lastOpenedAtUtc = DateTime.UtcNow;
            _suppressToleranceChanges = true;
            try
            {
                _toleranceBox.SelectedItem = Localization.T(_toleranceProfile);
            }
            finally
            {
                _suppressToleranceChanges = false;
            }
            _customTolerancePanel.Visible = _toleranceProfile == "Prilagođena";
            _status.Text = Localization.Status($"Projekat je učitan. Nastavak rada: titl #{_selectedIndex + 1} od {_cues.Count}.");
            _saveStatusLabel.Text = string.Format(Localization.T("Nastavak: titl {0} / {1} · izmenjeno {2}"), _selectedIndex + 1, _cues.Count, _modifiedRows.Count);
        }
        catch (Exception ex)
        {
            _status.Text = Localization.Status("Sačuvani projekat nije mogao da se učita: " + ex.Message);
        }
    }

    private void ApplyToleranceProfile()
    {
        if (_suppressToleranceChanges) return;
        if (_toleranceBox.SelectedItem is not string selected) return;
        _toleranceProfile = CanonicalToleranceProfile(selected);
        _customTolerancePanel.Visible = _toleranceProfile == "Prilagođena";

        (double strongLimit, double moderateLimit) = _toleranceProfile switch
        {
            "Stroga" => (0.25, 0.65),
            "Opuštena" => (0.75, 1.50),
            "Prilagođena" => (_customStrongLimitSeconds, _customModerateLimitSeconds),
            _ => (0.45, 1.00)
        };

        RecalculateStatuses(strongLimit, moderateLimit);
        _status.Text = string.Format(Localization.T("Tolerancija '{0}' je primenjena i odmah sačuvana u projektu."), Localization.T(_toleranceProfile));
    }

    private void LocalizeToleranceOptions()
    {
        _suppressToleranceChanges = true;
        try
        {
            _toleranceBox.Items.Clear();
            foreach (string profile in new[] { "Stroga", "Standardna", "Opuštena", "Prilagođena" })
                _toleranceBox.Items.Add(Localization.T(profile));
            _toleranceBox.SelectedItem = Localization.T(_toleranceProfile);
        }
        finally
        {
            _suppressToleranceChanges = false;
        }
    }

    private static string CanonicalToleranceProfile(string displayed) =>
        new[] { "Stroga", "Standardna", "Opuštena", "Prilagođena" }
            .FirstOrDefault(profile => string.Equals(Localization.T(profile), displayed, StringComparison.CurrentCulture))
        ?? "Standardna";

    private void ApplyCustomToleranceFromControls()
    {
        _customStrongLimitSeconds = (double)_customStrongLimit.Value;
        _customModerateLimitSeconds = (double)_customModerateLimit.Value;
        if (_customModerateLimitSeconds <= _customStrongLimitSeconds)
        {
            _customModerateLimitSeconds = Math.Min((double)_customModerateLimit.Maximum, _customStrongLimitSeconds + 0.05);
            _customModerateLimit.Value = (decimal)_customModerateLimitSeconds;
        }
        if (_toleranceProfile != "Prilagođena") return;

        RecalculateStatuses(_customStrongLimitSeconds, _customModerateLimitSeconds);
        _status.Text = Localization.Status($"Ručna tolerancija je primenjena: POUZDANO ≤ {_customStrongLimitSeconds:0.00} s, PROVERITI ≤ {_customModerateLimitSeconds:0.00} s.");
    }

    private void RecalculateStatuses(double strongLimit, double moderateLimit)
    {
        if (moderateLimit <= strongLimit) moderateLimit = strongLimit + 0.05;
        for (int i = 0; i < _signals.Count && i < _cues.Count; i++)
        {
            ReviewSignal current = _signals[i];
            if (current.DetectedSpeechStart is not TimeSpan speechStart)
            {
                _signals[i] = CopySignalWithStatus(current, "WEAK");
                continue;
            }

            double delta = Math.Abs((speechStart - _cues[i].Start).TotalSeconds);
            string status = delta <= strongLimit ? "STRONG" : delta <= moderateLimit ? "MODERATE" : "WEAK";
            _signals[i] = CopySignalWithStatus(current, status);
        }

        int selectedIndex = Math.Max(0, _selectedIndex);
        LoadRows();
        if (_grid.Rows.Count > 0) SelectRow(Math.Min(selectedIndex, _grid.Rows.Count - 1));
        _dirty = true;
        AutoSaveProject(force: true);
    }

    private static ReviewSignal CopySignalWithStatus(ReviewSignal source, string status)
    {
        return new ReviewSignal
        {
            CueNumber = source.CueNumber,
            WordCount = source.WordCount,
            MatchedWordCount = source.MatchedWordCount,
            Coverage = source.Coverage,
            AverageSimilarity = source.AverageSimilarity,
            IsAnchor = source.IsAnchor,
            FirstConfirmedWordPosition = source.FirstConfirmedWordPosition,
            FirstConfirmedWordStart = source.FirstConfirmedWordStart,
            DetectedSpeechStart = source.DetectedSpeechStart,
            DetectedSpeechEnd = source.DetectedSpeechEnd,
            FirstConfirmedWordConfidence = source.FirstConfirmedWordConfidence,
            OpeningPhraseWordCount = source.OpeningPhraseWordCount,
            OpeningPhraseMatchedWords = source.OpeningPhraseMatchedWords,
            OpeningPhraseCoverage = source.OpeningPhraseCoverage,
            OpeningPhraseSimilarity = source.OpeningPhraseSimilarity,
            OpeningPhraseContinuity = source.OpeningPhraseContinuity,
            OpeningPhraseConfidence = source.OpeningPhraseConfidence,
            OpeningPhraseStatus = status
        };
    }

    private string? BuildTimingWarning(int index, TimeSpan start, TimeSpan end)
    {
        var warnings = new List<string>();
        TimeSpan duration = end - start;
        if (duration < TimeSpan.FromMilliseconds(500)) warnings.Add(Localization.IsSerbian ? "Titl traje kraće od 0,5 sekundi." : "The subtitle is shorter than 0.5 seconds.");
        if (duration > TimeSpan.FromSeconds(10)) warnings.Add(Localization.IsSerbian ? "Titl traje duže od 10 sekundi." : "The subtitle is longer than 10 seconds.");
        if (index > 0)
        {
            TimeSpan gapBefore = start - _cues[index - 1].End;
            if (gapBefore < MinimumCueGap)
                warnings.Add(Localization.IsSerbian
                    ? $"Razmak od prethodnog titla je {Math.Max(0, gapBefore.TotalMilliseconds):0} ms; potrebno je najmanje 80 ms."
                    : $"The gap from the previous subtitle is {Math.Max(0, gapBefore.TotalMilliseconds):0} ms; at least 80 ms is required.");
        }
        if (index + 1 < _cues.Count)
        {
            TimeSpan gapAfter = _cues[index + 1].Start - end;
            if (gapAfter < MinimumCueGap)
                warnings.Add(Localization.IsSerbian
                    ? $"Razmak do sledećeg titla je {Math.Max(0, gapAfter.TotalMilliseconds):0} ms; potrebno je najmanje 80 ms."
                    : $"The gap to the next subtitle is {Math.Max(0, gapAfter.TotalMilliseconds):0} ms; at least 80 ms is required.");
        }
        return warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
    }

    private void InsertSubtitleAbove()
    {
        int insertIndex = _selectedIndex < 0 ? 0 : _selectedIndex;
        InsertSubtitleAt(insertIndex);
    }

    private void InsertSubtitleBelow()
    {
        int insertIndex = _selectedIndex < 0 ? _cues.Count : Math.Min(_cues.Count, _selectedIndex + 1);
        InsertSubtitleAt(insertIndex);
    }

    private void InsertSubtitleAt(int insertIndex)
    {
        (TimeSpan Start, TimeSpan End)? calculatedTimes = CalculateInsertedCueTimes(insertIndex);
        if (calculatedTimes is null)
        {
            Warn(Localization.IsSerbian
                ? "Nema dovoljno prostora za novi titl uz minimalni razmak od 80 ms."
                : "There is not enough room for a new subtitle with the minimum 80 ms gap.");
            return;
        }

        _undo.Push(Snapshot());
        _redo.Clear();
        (TimeSpan start, TimeSpan end) = calculatedTimes.Value;
        EnsureSignalCountMatchesCues();
        _cues.Insert(insertIndex, new SubtitleCue
        {
            Start = start,
            End = end,
            Text = Localization.IsSerbian ? "Novi titl" : "New subtitle"
        });
        _signals.Insert(insertIndex, new ReviewSignal
        {
            CueNumber = insertIndex + 1,
            DetectedSpeechStart = null,
            DetectedSpeechEnd = null,
            OpeningPhraseStatus = "WEAK"
        });
        ShiftModifiedRowsForInsert(insertIndex);
        _modifiedRows.Add(insertIndex);
        _subtitleAddCount++;
        _manualEditCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        _selectedIndex = insertIndex;
        RenumberSignals();
        FinishStructuralEdit("Novi red je dodat i projekat je automatski sačuvan.");
        _textBox.Focus();
        _textBox.SelectAll();
    }

    private void DeleteSelectedSubtitle()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _cues.Count) return;
        SubtitleCue cue = _cues[_selectedIndex];
        string question = string.IsNullOrWhiteSpace(cue.Text)
            ? (Localization.IsSerbian ? "Da li želiš da obrišeš izabrani red?" : "Do you want to delete the selected row?")
            : (Localization.IsSerbian ? $"Da li želiš da obrišeš titl #{_selectedIndex + 1}?\n\n{cue.Text}" : $"Do you want to delete subtitle #{_selectedIndex + 1}?\n\n{cue.Text}");
        if (MessageBox.Show(this, question, Localization.IsSerbian ? "Brisanje titla" : "Delete subtitle", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _undo.Push(Snapshot());
        _redo.Clear();
        int deletedIndex = _selectedIndex;
        _cues.RemoveAt(deletedIndex);
        if (deletedIndex < _signals.Count) _signals.RemoveAt(deletedIndex);
        ShiftModifiedRowsForDelete(deletedIndex);
        _subtitleDeleteCount++;
        _manualEditCount++;
        _lastEditedAtUtc = DateTime.UtcNow;
        _selectedIndex = _cues.Count == 0 ? -1 : Math.Min(deletedIndex, _cues.Count - 1);
        RenumberSignals();
        FinishStructuralEdit("Red je obrisan i projekat je automatski sačuvan.");
    }

    private (TimeSpan Start, TimeSpan End)? CalculateInsertedCueTimes(int insertIndex)
    {
        TimeSpan minimumDuration = TimeSpan.FromMilliseconds(500);
        TimeSpan preferredDuration = TimeSpan.FromSeconds(2);
        TimeSpan gap = MinimumCueGap;
        SubtitleCue? previous = insertIndex > 0 ? _cues[insertIndex - 1] : null;
        SubtitleCue? next = insertIndex < _cues.Count ? _cues[insertIndex] : null;

        if (previous is not null && next is not null)
        {
            TimeSpan availableStart = previous.End + gap;
            TimeSpan availableEnd = next.Start - gap;
            if (availableEnd - availableStart >= minimumDuration)
                return (availableStart, (availableStart + preferredDuration) < availableEnd ? availableStart + preferredDuration : availableEnd);

            return null;
        }

        if (next is not null)
        {
            TimeSpan end = next.Start > gap ? next.Start - gap : next.Start;
            TimeSpan start = (end - preferredDuration) > TimeSpan.Zero ? end - preferredDuration : TimeSpan.Zero;
            if (end - start < minimumDuration) return null;
            return (start, end);
        }

        if (previous is not null)
        {
            TimeSpan start = previous.End + gap;
            return (start, start + preferredDuration);
        }

        return (TimeSpan.Zero, preferredDuration);
    }

    private void FinishStructuralEdit(string message)
    {
        int targetIndex = _selectedIndex;
        var viewport = CaptureGridViewport();
        LoadRows();
        _selectedIndex = targetIndex;
        if (targetIndex >= 0) SelectRow(targetIndex);
        RestoreGridViewport(viewport, targetIndex);
        LoadSelection();
        QueuePlayerPreviewRefresh(preservePosition: true);
        UpdateHistoryButtons();
        _dirty = true;
        _saveStatusLabel.Text = Localization.T("Izmene nisu sačuvane u SRT");
        AutoSaveProject(force: true);
        _status.Text = Localization.Status(message);
    }

    private void EnsureSignalCountMatchesCues()
    {
        while (_signals.Count < _cues.Count)
        {
            _signals.Add(new ReviewSignal
            {
                CueNumber = _signals.Count + 1,
                OpeningPhraseStatus = "WEAK"
            });
        }
        while (_signals.Count > _cues.Count) _signals.RemoveAt(_signals.Count - 1);
    }

    private void RenumberSignals()
    {
        for (int i = 0; i < _signals.Count; i++)
        {
            ReviewSignal signal = _signals[i];
            if (signal.CueNumber == i + 1) continue;
            _signals[i] = CloneSignal(signal, i + 1);
        }
    }

    private void ShiftModifiedRowsForInsert(int insertIndex)
    {
        int[] old = _modifiedRows.ToArray();
        _modifiedRows.Clear();
        foreach (int index in old) _modifiedRows.Add(index >= insertIndex ? index + 1 : index);
    }

    private void ShiftModifiedRowsForDelete(int deletedIndex)
    {
        int[] old = _modifiedRows.ToArray();
        _modifiedRows.Clear();
        foreach (int index in old)
        {
            if (index == deletedIndex) continue;
            _modifiedRows.Add(index > deletedIndex ? index - 1 : index);
        }
    }

    private static ReviewSignal CloneSignal(ReviewSignal source, int? cueNumber = null) => new()
    {
        CueNumber = cueNumber ?? source.CueNumber,
        WordCount = source.WordCount,
        MatchedWordCount = source.MatchedWordCount,
        Coverage = source.Coverage,
        AverageSimilarity = source.AverageSimilarity,
        IsAnchor = source.IsAnchor,
        FirstConfirmedWordPosition = source.FirstConfirmedWordPosition,
        FirstConfirmedWordStart = source.FirstConfirmedWordStart,
        DetectedSpeechStart = source.DetectedSpeechStart,
        DetectedSpeechEnd = source.DetectedSpeechEnd,
        FirstConfirmedWordConfidence = source.FirstConfirmedWordConfidence,
        OpeningPhraseWordCount = source.OpeningPhraseWordCount,
        OpeningPhraseMatchedWords = source.OpeningPhraseMatchedWords,
        OpeningPhraseCoverage = source.OpeningPhraseCoverage,
        OpeningPhraseSimilarity = source.OpeningPhraseSimilarity,
        OpeningPhraseContinuity = source.OpeningPhraseContinuity,
        OpeningPhraseConfidence = source.OpeningPhraseConfidence,
        OpeningPhraseStatus = source.OpeningPhraseStatus
    };

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        _dirty = true;
        AutoSaveProject();
        _status.Text = Localization.Status("Poslednja izmena je poništena.");
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        _dirty = true;
        AutoSaveProject();
        _status.Text = Localization.Status("Izmena je ponovljena.");
    }

    private async Task SaveAsAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = Localization.Filter(SubtitleFormatWriter.SaveFilter),
            DefaultExt = "srt",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_defaultOutputPath) + "_edited.srt",
            InitialDirectory = Path.GetDirectoryName(_defaultOutputPath)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // Najpre zadržavamo samostalno stanje trenutno otvorenog projekta.
        AutoSaveProject(force: true);
        await SubtitleFormatWriter.SaveAsync(dialog.FileName, _cues, CancellationToken.None);

        // Svaki Save As / rezervni SRT dobija sopstveni .subtitleproject.json.
        _defaultOutputPath = dialog.FileName;
        _projectPath = WorkspacePaths.GetProjectPath(_defaultOutputPath);
        _grid.Invalidate();
        _dirty = true;
        AutoSaveProject(force: true);
        _status.Text = Localization.Status("Sačuvano: " + dialog.FileName);
        _saveStatusLabel.Text = string.Format(Localization.T("Sačuvano · izmenjeno {0} · titl {1} / {2}"), _modifiedRows.Count, _selectedIndex + 1, _cues.Count);
        MessageBox.Show(this, Localization.T("Titl i njegovo projektno stanje sačuvani su kao nezavisna kopija."), "SubtitleBoom", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ExportYouTubePackageAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = Localization.Filter("Osnovni naziv YouTube paketa|*.srt"),
            DefaultExt = "srt",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_defaultOutputPath) + "_youtube.srt",
            InitialDirectory = Path.GetDirectoryName(_defaultOutputPath)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await SubtitleFormatWriter.ExportYouTubeSetAsync(dialog.FileName, _cues, CancellationToken.None);
        _status.Text = Localization.Status("YouTube paket je izvezen: SRT, VTT i SBV.");
        MessageBox.Show(this, Localization.T("Napravljeni su SRT, WebVTT i YouTube SBV fajlovi."), "SubtitleBoom", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private ReviewSignal? CurrentSignal() => _selectedIndex >= 0 && _selectedIndex < _signals.Count ? _signals[_selectedIndex] : null;

    private EditorSnapshot Snapshot() => new(
        _cues.Select(c => new CueState(c.Start, c.End, c.Text)).ToList(),
        _signals.Select(signal => CloneSignal(signal)).ToList(),
        _modifiedRows.ToHashSet(),
        _selectedIndex,
        _subtitleAddCount,
        _subtitleDeleteCount);

    private void Restore(EditorSnapshot state)
    {
        var viewport = CaptureGridViewport();
        _cues.Clear();
        _cues.AddRange(state.Cues.Select(c => new SubtitleCue { Start = c.Start, End = c.End, Text = c.Text }));
        _signals.Clear();
        _signals.AddRange(state.Signals.Select(signal => CloneSignal(signal)));
        _modifiedRows.Clear();
        foreach (int index in state.ModifiedRows.Where(i => i >= 0 && i < _cues.Count)) _modifiedRows.Add(index);
        _subtitleAddCount = state.SubtitleAddCount;
        _subtitleDeleteCount = state.SubtitleDeleteCount;
        RenumberSignals();
        int targetIndex = _cues.Count == 0 ? -1 : Math.Clamp(state.SelectedIndex, 0, _cues.Count - 1);
        _selectedIndex = targetIndex;
        LoadRows();
        _selectedIndex = targetIndex;
        if (targetIndex >= 0)
        {
            SelectRow(targetIndex);
            RestoreGridViewport(viewport, targetIndex);
            LoadSelection();
        }
        QueuePlayerPreviewRefresh(preservePosition: true);
        UpdateHistoryButtons();
    }

    private void SelectRow(int index)
    {
        if (index < 0 || index >= _grid.Rows.Count) return;
        _suppressGridSelectionChanged = true;
        try
        {
            // Aktivna ćelija, vizuelno označeni red i sadržaj editora moraju
            // preći na isti titl kao jedna operacija. DataGridView inače šalje
            // više SelectionChanged događaja sa privremeno različitim redovima.
            _grid.CurrentCell = _grid.Rows[index].Cells[0];
            _grid.ClearSelection();
            _grid.Rows[index].Selected = true;
        }
        finally
        {
            _suppressGridSelectionChanged = false;
        }
        LoadSelection();
    }

    private void UpdateHistoryButtons()
    {
        _undoButton.Enabled = _undo.Count > 0;
        _redoButton.Enabled = _redo.Count > 0;
    }

    private void DisposePlayer()
    {
        _playerTimer.Stop();
        _autoSaveTimer.Stop();
        try { _mediaPlayer?.Stop(); } catch { }
        _media?.Dispose();
        try { if (File.Exists(_previewSrtPath)) File.Delete(_previewSrtPath); } catch { }
        _mediaPlayer?.Dispose();
        _libVlc?.Dispose();
    }

    private void Warn(string message) => MessageBox.Show(this, Localization.Status(message), "SubtitleBoom", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static bool TryParseFlexible(string value, out TimeSpan result)
    {
        string normalized = value.Trim().Replace('.', ',');
        string[] formats =
        {
            @"hh\:mm\:ss\,fff", @"h\:mm\:ss\,fff",
            @"mm\:ss\,fff", @"m\:ss\,fff",
            @"hh\:mm\:ss", @"h\:mm\:ss",
            @"mm\:ss", @"m\:ss"
        };
        return TimeSpan.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParse(string value, out TimeSpan result) => TryParseFlexible(value, out result);

    private static string Format(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";

    private sealed class RowItem
    {
        public int Number { get; init; }
        public required string Start { get; init; }
        public required string Speech { get; init; }
        public required string Status { get; init; }
        public required string Text { get; init; }
    }

    private sealed class ProjectState
    {
        public int Version { get; set; }
        public string ProjectAppVersion { get; set; } = "4.1.1";
        public int ProjectFormatVersion { get; set; } = 8;
        public string? MediaPath { get; set; }
        public string? SrtPath { get; set; }
        public int SelectedIndex { get; set; }
        public DateTime SavedAtUtc { get; set; }
        public long PlayerPositionMilliseconds { get; set; }
        public string? ModelFileName { get; set; }
        public string? Language { get; set; }
        public string ToleranceProfile { get; set; } = "Standardna";
        public double CustomStrongLimitSeconds { get; set; } = 0.45;
        public double CustomModerateLimitSeconds { get; set; } = 1.00;
        public long InitialProcessingMilliseconds { get; set; }
        public DateTime? InitialProcessedAtUtc { get; set; }
        public bool CreatedByBatch { get; set; }
        public Dictionary<string, long> ProcessingPhaseMilliseconds { get; set; } = new();
        public DateTime ProjectCreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastOpenedAtUtc { get; set; }
        public long LastLoadMilliseconds { get; set; }
        public long TotalLoadMilliseconds { get; set; }
        public int OpenCount { get; set; }
        public DateTime? LastEditedAtUtc { get; set; }
        public int ManualEditCount { get; set; }
        public int ApplyCount { get; set; }
        public int AutoSaveCount { get; set; }
        public int SubtitleAddCount { get; set; }
        public int SubtitleDeleteCount { get; set; }
        public List<int> ModifiedRows { get; set; } = new();
        public List<ProjectCue> Cues { get; set; } = new();
    }

    private sealed class ProjectCue
    {
        public long StartMilliseconds { get; set; }
        public long EndMilliseconds { get; set; }
        public string? Text { get; set; }
        public long? SpeechStartMilliseconds { get; set; }
        public long? SpeechEndMilliseconds { get; set; }
        public long? StartOffsetMilliseconds { get; set; }
        public long? EndOffsetMilliseconds { get; set; }
        public double Confidence { get; set; }
        public string? OpeningPhraseStatus { get; set; }
    }

    private sealed record CueState(TimeSpan Start, TimeSpan End, string Text);
    private sealed record EditorSnapshot(
        List<CueState> Cues,
        List<ReviewSignal> Signals,
        HashSet<int> ModifiedRows,
        int SelectedIndex,
        int SubtitleAddCount,
        int SubtitleDeleteCount);
}
