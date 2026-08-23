using System.Net.Http.Headers;
using SubtitleAligner.Models;

namespace SubtitleAligner;

public sealed class ModelManagerForm : Form
{
    private readonly string _modelsDirectory;
    private readonly TableLayoutPanel _list = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 4 };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 22 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CancellationTokenSource _cancellation = new();

    public ModelManagerForm(string modelsDirectory)
    {
        _modelsDirectory = modelsDirectory;
        Directory.CreateDirectory(_modelsDirectory);

        Text = "Dodatni Whisper modeli";
        Width = 670;
        Height = 330;
        MinimumSize = new Size(600, 280);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(12, 10, 12, 4),
            Text = "Tiny i Base su standardni modeli. Ovde po potrebi možeš naknadno preuzeti veće modele."
        };

        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _list.Padding = new Padding(12);

        Controls.Add(_list);
        Controls.Add(_progress);
        Controls.Add(_status);
        Controls.Add(intro);
        FormClosing += (_, _) => _cancellation.Cancel();
        RebuildRows();
        Localization.Apply(this);
    }

    private void RebuildRows()
    {
        _list.SuspendLayout();
        _list.Controls.Clear();
        _list.RowStyles.Clear();
        _list.RowCount = 1;

        AddHeader("Model", 0);
        AddHeader("Veličina", 1);
        AddHeader("Status", 2);
        AddHeader("Akcija", 3);

        foreach (var model in WhisperModelCatalog.OptionalModels)
        {
            int row = _list.RowCount++;
            _list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            string path = Path.Combine(_modelsDirectory, model.FileName);
            bool installed = File.Exists(path) && new FileInfo(path).Length >= model.MinimumBytes;

            _list.Controls.Add(new Label { Text = model.ToString(), AutoSize = true, Margin = new Padding(3, 10, 3, 3) }, 0, row);
            _list.Controls.Add(new Label { Text = ApproximateSize(model.Id), AutoSize = true, Margin = new Padding(3, 10, 3, 3) }, 1, row);
            _list.Controls.Add(new Label { Text = installed ? "Instaliran ✓" : "Nije instaliran", AutoSize = true, Margin = new Padding(3, 10, 3, 3) }, 2, row);

            var button = new Button { Text = installed ? "Ukloni" : "Preuzmi", AutoSize = true, Tag = model };
            button.Click += async (_, _) =>
            {
                if (installed)
                {
                    if (MessageBox.Show(this,
                        Localization.IsSerbian ? $"Ukloniti model {model.DisplayName}?" : $"Remove model {model.DisplayName}?",
                        Localization.IsSerbian ? "Modeli" : "Models", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        File.Delete(path);
                        RebuildRows();
                    }
                }
                else
                {
                    await DownloadModelAsync(model, button);
                }
            };
            _list.Controls.Add(button, 3, row);
        }
        _list.ResumeLayout();
        Localization.Apply(_list);
    }

    private async Task DownloadModelAsync(WhisperModelOption model, Button sourceButton)
    {
        if (string.IsNullOrWhiteSpace(model.DownloadUrl)) return;
        string destination = Path.Combine(_modelsDirectory, model.FileName);
        string temporary = destination + ".download";

        try
        {
            SetButtonsEnabled(false);
            _progress.Value = 0;
            _status.Text = Localization.Status($"Preuzimam {model.DisplayName}…");

            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SubtitleBoom", "1.0"));
            using var response = await client.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _cancellation.Token);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            long received = 0;
            await using (Stream input = await response.Content.ReadAsStreamAsync(_cancellation.Token))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                byte[] buffer = new byte[1024 * 128];
                int read;
                while ((read = await input.ReadAsync(buffer, _cancellation.Token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), _cancellation.Token);
                    received += read;
                    if (total > 0)
                    {
                        int percent = (int)Math.Clamp(received * 100 / total.Value, 0, 100);
                        _progress.Value = percent;
                        _status.Text = Localization.Status($"Preuzimam {model.DisplayName}: {percent}%");
                    }
                }

                await output.FlushAsync(_cancellation.Token);
            }

            // Tek po izlasku iz await using bloka Windows oslobađa .download fajl.
            if (new FileInfo(temporary).Length < model.MinimumBytes)
                throw new InvalidDataException("Preuzeti fajl je nepotpun ili neispravan.");

            File.Move(temporary, destination, true);
            _status.Text = Localization.Status($"{model.DisplayName} je instaliran.");
            RebuildRows();
        }
        catch (OperationCanceledException) { _status.Text = Localization.Status("Preuzimanje je otkazano."); }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.T("Model nije mogao da se preuzme.") + "\n\n" + ex.Message, Localization.T("Modeli"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = Localization.Status("Preuzimanje nije uspelo.");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            SetButtonsEnabled(true);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (Control control in _list.Controls)
            if (control is Button button) button.Enabled = enabled;
    }

    private void AddHeader(string text, int column) =>
        _list.Controls.Add(new Label { Text = text, Font = new Font(Font, FontStyle.Bold), AutoSize = true }, column, 0);

    private static string ApproximateSize(string id) => id switch
    {
        "small" => "oko 466 MB",
        "medium" => "oko 1,5 GB",
        "large-v3" => "oko 3,1 GB",
        _ => ""
    };
}
