using System.ComponentModel;
using System.Text.Json;
using SubtitleAligner.Models;
using SubtitleAligner.Services;

namespace SubtitleAligner;

public sealed class BatchManagerForm : Form
{
    private readonly BindingList<BatchJob> _jobs = [];
    private readonly IReadOnlyList<WhisperModelOption> _models;
    private readonly string _defaultModelDisplayName;
    private readonly string _defaultLanguageDisplayName;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
    private readonly Button _startButton = new() { Text = "POKRENI SVE", Width = 130, Height = 36 };
    private readonly Button _pauseButton = new() { Text = "Pauziraj posle trenutnog", Width = 185, Height = 36, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Otkaži trenutni", Width = 130, Height = 36, Enabled = false };
    private readonly Button _retryButton = new() { Text = "Ponovi neuspele", Width = 140, Height = 36 };
    private readonly ProgressBar _overallProgress = new() { Dock = DockStyle.Top, Height = 20 };
    private readonly Label _summary = new() { Text = "0 poslova", AutoSize = true };
    private readonly CheckBox _exportVtt = new() { Text = "WebVTT (.vtt)", AutoSize = true, Checked = true, Margin = new Padding(15, 8, 3, 3) };
    private readonly CheckBox _exportSbv = new() { Text = "YouTube SBV (.sbv)", AutoSize = true, Checked = true, Margin = new Padding(8, 8, 3, 3) };
    private readonly Label _folderRule = new()
    {
        AutoSize = true,
        Text = "Svaki posao ostaje u svom folderu. Ulaz: video/audio + TXT. Izlaz: SRT, izabrani YouTube formati i SubtitleBoom_Data.",
        ForeColor = Color.DarkGreen,
        Margin = new Padding(0, 7, 0, 5)
    };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f)
    };

    private CancellationTokenSource? _currentCancellation;
    private bool _running;
    private bool _pauseAfterCurrent;

    public BatchManagerForm(IReadOnlyList<WhisperModelOption> models, int selectedModelIndex, string selectedLanguageCode)
    {
        _models = models;
        _defaultModelDisplayName = models.Count == 0
            ? "Tiny — brzo"
            : models[Math.Clamp(selectedModelIndex, 0, models.Count - 1)].DisplayName;
        _defaultLanguageDisplayName = ProcessingLanguages.ByCode(selectedLanguageCode).ToString();

        Text = "SubtitleBoom v1.0 — Batch Processing by Folder";
        Width = 1280;
        Height = 780;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);

        BuildGrid();
        BuildLayout();
        WireEvents();
        UpdateSummary();
        Localization.Apply(this);
    }

    private void BuildGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchJob.Name),
            HeaderText = "Projekat",
            Width = 150
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchJob.SourceMediaPath),
            HeaderText = "Video / audio",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 29,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchJob.SourceSubtitlePath),
            HeaderText = "TXT tekst (dvoklik za izbor)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25,
            ReadOnly = true
        });

        var languageColumn = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(BatchJob.LanguageDisplayName),
            HeaderText = "Jezik govora",
            Width = 115,
            FlatStyle = FlatStyle.Flat
        };
        languageColumn.Items.AddRange(ProcessingLanguages.All.Select(x => x.ToString()).Cast<object>().ToArray());
        _grid.Columns.Add(languageColumn);

        var modelColumn = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(BatchJob.ModelDisplayName),
            HeaderText = "Model / brzina",
            Width = 195,
            FlatStyle = FlatStyle.Flat
        };
        foreach (WhisperModelOption model in _models) modelColumn.Items.Add(model.DisplayName);
        _grid.Columns.Add(modelColumn);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchJob.StatusText),
            HeaderText = "Status",
            Width = 190,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchJob.Progress),
            HeaderText = "%",
            Width = 50,
            ReadOnly = true
        });
        _grid.DataSource = _jobs;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));

        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        fileButtons.Controls.Add(MakeButton("Dodaj video fajlove…", AddVideos));
        fileButtons.Controls.Add(MakeButton("Dodaj TXT fajlove…", AddTextFiles));
        fileButtons.Controls.Add(MakeButton("Dodaj folder(e)…", AddFolders));
        fileButtons.Controls.Add(MakeButton("Ukloni izabrani", RemoveSelected));
        fileButtons.Controls.Add(MakeButton("Očisti listu", ClearJobs));
        fileButtons.Controls.Add(_summary);
        fileButtons.Controls.Add(new Label { Text = "Dodatni izlazi:", AutoSize = true, Margin = new Padding(20, 8, 2, 0) });
        fileButtons.Controls.Add(_exportVtt);
        fileButtons.Controls.Add(_exportSbv);
        root.Controls.Add(fileButtons);

        root.Controls.Add(_folderRule);
        root.Controls.Add(_grid);

        var commandPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        commandPanel.Controls.Add(_startButton);
        commandPanel.Controls.Add(_pauseButton);
        commandPanel.Controls.Add(_cancelButton);
        commandPanel.Controls.Add(_retryButton);
        root.Controls.Add(commandPanel);

        var lower = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        lower.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lower.Controls.Add(_overallProgress);
        lower.Controls.Add(_log);
        root.Controls.Add(lower);
        Controls.Add(root);
    }

    private Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }

    private void WireEvents()
    {
        _startButton.Click += async (_, _) => await StartQueueAsync();
        _pauseButton.Click += (_, _) =>
        {
            _pauseAfterCurrent = !_pauseAfterCurrent;
            _pauseButton.Text = Localization.T(_pauseAfterCurrent ? "Nastavi posle trenutnog" : "Pauziraj posle trenutnog");
            Log(Localization.IsSerbian
                ? (_pauseAfterCurrent ? "Red će se pauzirati nakon trenutnog posla." : "Pauza je ukinuta.")
                : (_pauseAfterCurrent ? "The queue will pause after the current job." : "Pause has been canceled."));
        };
        _cancelButton.Click += (_, _) => _currentCancellation?.Cancel();
        _retryButton.Click += (_, _) => RetryFailed();
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 2) PickTextForRow(e.RowIndex);
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex is 3 or 4)
            {
                BatchJob job = _jobs[e.RowIndex];
                job.Status = BatchJobStatus.Waiting;
                job.StatusText = Localization.Status("Čeka");
            }
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.Value is not string text) return;
            if (e.ColumnIndex == 4 || e.ColumnIndex == 5)
            {
                e.Value = text.StartsWith("GREŠKA: ", StringComparison.Ordinal)
                    ? Localization.T("GREŠKA:") + " " + Localization.T(text[8..])
                    : Localization.T(text);
                e.FormattingApplied = true;
            }
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
        FormClosing += (_, e) =>
        {
            if (!_running) return;
            if (MessageBox.Show(this, Localization.T("Batch obrada još traje. Otkaži trenutni posao i zatvori?"), "Batch Mode",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _currentCancellation?.Cancel();
        };
    }

    private void AddVideos()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = Localization.Filter("Video i audio|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|Svi fajlovi|*.*")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (string path in dialog.FileNames) AddOrUpdateVideo(path);
        AutoPairFromMediaFolders();
    }

    private void AddTextFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = Localization.Filter("Tekstualni fajlovi|*.txt|Svi fajlovi|*.*")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        PairTextFiles(dialog.FileNames);
    }

    private void AddFolders()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Localization.T("Izaberi folder. Program će pronaći video/audio + TXT u njemu i njegovim podfolderima.")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string[] media = Directory.EnumerateFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
            .Where(IsMedia).ToArray();
        foreach (string path in media) AddOrUpdateVideo(path);
        AutoPairFromMediaFolders();
    }

    private void AddOrUpdateVideo(string path)
    {
        if (_jobs.Any(x => string.Equals(x.SourceMediaPath, path, StringComparison.OrdinalIgnoreCase))) return;
        _jobs.Add(new BatchJob
        {
            Name = Path.GetFileNameWithoutExtension(path),
            SourceMediaPath = path,
            MediaPath = path,
            ModelDisplayName = _defaultModelDisplayName,
            LanguageDisplayName = _defaultLanguageDisplayName,
            Status = BatchJobStatus.Waiting,
            StatusText = Localization.T("Čeka TXT uparivanje")
        });
        UpdateSummary();
    }

    private void PairTextFiles(IEnumerable<string> files)
    {
        foreach (string textPath in files.Where(IsText))
        {
            string textDirectory = Path.GetDirectoryName(textPath) ?? string.Empty;
            string key = NormalizeStem(Path.GetFileNameWithoutExtension(textPath));
            BatchJob? exact = _jobs.FirstOrDefault(x =>
                string.IsNullOrWhiteSpace(x.SourceSubtitlePath) &&
                string.Equals(Path.GetDirectoryName(x.SourceMediaPath), textDirectory, StringComparison.OrdinalIgnoreCase) &&
                NormalizeStem(Path.GetFileNameWithoutExtension(x.SourceMediaPath)) == key);

            exact ??= _jobs.FirstOrDefault(x =>
                string.IsNullOrWhiteSpace(x.SourceSubtitlePath) &&
                string.Equals(Path.GetDirectoryName(x.SourceMediaPath), textDirectory, StringComparison.OrdinalIgnoreCase));

            exact ??= _jobs.FirstOrDefault(x =>
                string.IsNullOrWhiteSpace(x.SourceSubtitlePath) &&
                (NormalizeStem(Path.GetFileNameWithoutExtension(x.SourceMediaPath)).Contains(key) ||
                 key.Contains(NormalizeStem(Path.GetFileNameWithoutExtension(x.SourceMediaPath)))));

            if (exact is not null)
            {
                exact.SourceSubtitlePath = textPath;
                exact.SubtitlePath = textPath;
                exact.StatusText = Localization.Status("Čeka");
                exact.Status = BatchJobStatus.Waiting;
            }
            else
            {
                Log("Nije pronađen odgovarajući video za TXT: " + textPath);
            }
        }
        RefreshGrid();
    }

    private void AutoPairFromMediaFolders()
    {
        foreach (BatchJob job in _jobs.Where(x => string.IsNullOrWhiteSpace(x.SourceSubtitlePath)))
        {
            string directory = Path.GetDirectoryName(job.SourceMediaPath) ?? string.Empty;
            if (!Directory.Exists(directory)) continue;
            string mediaStem = NormalizeStem(Path.GetFileNameWithoutExtension(job.SourceMediaPath));
            string[] textFiles = Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly).ToArray();
            string? match = textFiles.FirstOrDefault(x => NormalizeStem(Path.GetFileNameWithoutExtension(x)) == mediaStem)
                            ?? (textFiles.Length == 1 ? textFiles[0] : null)
                            ?? textFiles.FirstOrDefault(x =>
                                NormalizeStem(Path.GetFileNameWithoutExtension(x)).Contains(mediaStem) ||
                                mediaStem.Contains(NormalizeStem(Path.GetFileNameWithoutExtension(x))));
            if (match is null) continue;
            job.SourceSubtitlePath = match;
            job.SubtitlePath = match;
            job.StatusText = Localization.Status("Čeka");
        }
        RefreshGrid();
    }

    private void PickTextForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _jobs.Count) return;
        using var dialog = new OpenFileDialog
        {
            Filter = Localization.Filter("Tekstualni fajlovi|*.txt|Svi fajlovi|*.*"),
            InitialDirectory = Path.GetDirectoryName(_jobs[rowIndex].SourceMediaPath)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _jobs[rowIndex].SourceSubtitlePath = dialog.FileName;
        _jobs[rowIndex].SubtitlePath = dialog.FileName;
        _jobs[rowIndex].Status = BatchJobStatus.Waiting;
        _jobs[rowIndex].StatusText = Localization.Status("Čeka");
        RefreshGrid();
    }

    private void RemoveSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is BatchJob job) _jobs.Remove(job);
        UpdateSummary();
    }

    private void ClearJobs()
    {
        if (_running) return;
        _jobs.Clear();
        UpdateSummary();
    }

    private async Task StartQueueAsync()
    {
        if (_running) return;
        _grid.EndEdit();
        if (_jobs.Count == 0)
        {
            MessageBox.Show(this, Localization.T("Dodaj najmanje jedan video/audio i odgovarajući TXT."), "Batch Mode");
            return;
        }
        if (_jobs.Any(x => string.IsNullOrWhiteSpace(x.SourceSubtitlePath) || !File.Exists(x.SourceSubtitlePath) || !IsText(x.SourceSubtitlePath)))
        {
            MessageBox.Show(this, Localization.T("Neki video nema uparen TXT. Dvoklikni polje TXT i izaberi odgovarajući tekstualni fajl."), "Batch Mode",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        SetUiRunning(true);
        _pauseAfterCurrent = false;
        _pauseButton.Text = Localization.T("Pauziraj posle trenutnog");
        int completedBefore = _jobs.Count(x => x.Status == BatchJobStatus.Completed);
        int processable = _jobs.Count(x => x.Status is BatchJobStatus.Waiting or BatchJobStatus.Failed or BatchJobStatus.Cancelled);
        int processed = 0;

        try
        {
            foreach (BatchJob job in _jobs.Where(x => x.Status is BatchJobStatus.Waiting or BatchJobStatus.Failed or BatchJobStatus.Cancelled).ToList())
            {
                if (_pauseAfterCurrent) break;
                PrepareJobPaths(job);
                job.Status = BatchJobStatus.Processing;
                job.StatusText = Localization.Status("Pripremam obradu…");
                job.Progress = 0;
                RefreshGrid();

                try
                {
                    _currentCancellation = new CancellationTokenSource();
                    var service = new BatchProcessingService(Path.Combine(AppContext.BaseDirectory, "runtime"));
                    await service.ProcessAsync(job,
                        (percent, text) => UpdateJob(job, percent, text),
                        text => Log($"[{job.Name}] {text}"),
                        _currentCancellation.Token);
                    job.Status = BatchJobStatus.Completed;
                    job.StatusText = Localization.Status("Završeno");
                    job.Progress = 100;
                    Log($"[{job.Name}] ZAVRŠENO: {job.OutputSrtPath}");
                }
                catch (OperationCanceledException)
                {
                    job.Status = BatchJobStatus.Cancelled;
                    job.StatusText = Localization.Status("Otkazano — može se nastaviti");
                    Log($"[{job.Name}] Otkazano. Završeni segmenti su sačuvani.");
                }
                catch (Exception ex)
                {
                    job.Status = BatchJobStatus.Failed;
                    job.StatusText = Localization.Status("GREŠKA: " + ex.Message);
                    job.ErrorMessage = ex.ToString();
                    Log($"[{job.Name}] GREŠKA: {ex}");
                }
                finally
                {
                    _currentCancellation?.Dispose();
                    _currentCancellation = null;
                }

                processed++;
                _overallProgress.Value = Math.Clamp((int)Math.Round((completedBefore + processed) * 100d / Math.Max(1, completedBefore + processable)), 0, 100);
                RefreshGrid();
                await SaveQueueAsync();
            }
        }
        finally
        {
            _running = false;
            SetUiRunning(false);
            UpdateSummary();
        }
    }

    private void PrepareJobPaths(BatchJob job)
    {
        WhisperModelOption? selectedModel = _models.FirstOrDefault(x => x.DisplayName == job.ModelDisplayName);
        if (selectedModel is null) throw new InvalidDataException($"Model nije pronađen za posao '{job.Name}'.");
        job.ModelFileName = selectedModel.FileName;
        job.ModelDisplayName = selectedModel.DisplayName;
        job.Language = ProcessingLanguages.CodeFromDisplay(job.LanguageDisplayName);

        string mediaDirectory = Path.GetDirectoryName(job.SourceMediaPath)
            ?? throw new InvalidDataException("Video/audio nema važeći folder.");
        job.ProjectDirectory = mediaDirectory;
        job.MediaPath = job.SourceMediaPath;
        job.SubtitlePath = job.SourceSubtitlePath;
        job.OutputSrtPath = Path.Combine(mediaDirectory, Path.GetFileNameWithoutExtension(job.SourceMediaPath) + "_aligned.srt");
        job.ExportVtt = _exportVtt.Checked;
        job.ExportSbv = _exportSbv.Checked;
    }

    private async Task SaveQueueAsync()
    {
        try
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleBoom");
            Directory.CreateDirectory(appData);
            string path = Path.Combine(appData, "SubtitleBoom_BatchQueue.json");
            string json = JsonSerializer.Serialize(_jobs.ToList(), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch { }
    }

    private void RetryFailed()
    {
        foreach (BatchJob job in _jobs.Where(x => x.Status is BatchJobStatus.Failed or BatchJobStatus.Cancelled))
        {
            job.Status = BatchJobStatus.Waiting;
            job.StatusText = Localization.Status("Čeka ponovno pokretanje");
            job.Progress = 0;
        }
        RefreshGrid();
    }

    private void UpdateJob(BatchJob job, int percent, string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateJob(job, percent, text));
            return;
        }
        job.Progress = Math.Clamp(percent, 0, 100);
        job.StatusText = Localization.Status(text);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshGrid);
            return;
        }
        foreach (BatchJob job in _jobs)
            job.StatusText = Localization.Status(job.StatusText);
        _grid.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int completed = _jobs.Count(x => x.Status == BatchJobStatus.Completed);
        int failed = _jobs.Count(x => x.Status == BatchJobStatus.Failed);
        _summary.Text = $"   {_jobs.Count} {Localization.T("poslova")} | {completed} {Localization.T("završeno")} | {failed} {Localization.T("grešaka")}";
    }

    private void SetUiRunning(bool running)
    {
        _startButton.Enabled = !running;
        _pauseButton.Enabled = running;
        _cancelButton.Enabled = running;
        _grid.ReadOnly = running;
    }

    private void Log(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(text));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {Localization.Status(text)}{Environment.NewLine}");
    }

    private static string NormalizeStem(string name)
    {
        string lower = name.ToLowerInvariant();
        string[] removable = ["aligned", "subtitle", "subtitles", "titl", "titlovi", "text", "tekst", "english", "serbian", "croatian", "eng", "sr", "hr", "en"];
        foreach (string token in removable) lower = lower.Replace(token, string.Empty);
        return new string(lower.Where(char.IsLetterOrDigit).ToArray());
    }

    private static bool IsMedia(string path)
        => new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsText(string path)
        => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);
}
