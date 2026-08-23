using System.Diagnostics;
using SubtitleAligner.Models;

namespace SubtitleAligner;

public sealed class WaveformCueEditEventArgs : EventArgs
{
    public WaveformCueEditEventArgs(TimeSpan start, TimeSpan end)
    {
        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }
    public TimeSpan End { get; }
}

public sealed class WaveformNeighborCueEventArgs : EventArgs
{
    public WaveformNeighborCueEventArgs(int offset) => Offset = offset;
    public int Offset { get; }
}

public sealed class WaveformPreviewControl : Control
{
    private static readonly TimeSpan MinimumCueGap = TimeSpan.FromMilliseconds(80);
    private enum DragMode
    {
        None,
        ResizeStart,
        ResizeEnd,
        Move
    }

    private float[] _samples = Array.Empty<float>();
    private TimeSpan _windowStart;
    private TimeSpan _windowEnd;
    private TimeSpan _cueStart;
    private TimeSpan _cueEnd;
    private TimeSpan? _speechStart;
    private TimeSpan? _speechEnd;
    private TimeSpan? _previousStart;
    private TimeSpan? _previousEnd;
    private TimeSpan? _nextStart;
    private TimeSpan? _nextEnd;
    private int _cueNumber;
    private TimeSpan _playhead;
    private int _requestVersion;
    private bool _loading;
    private string? _error;
    private DragMode _dragMode;
    private int _dragOriginX;
    private TimeSpan _dragOriginalStart;
    private TimeSpan _dragOriginalEnd;
    private bool _dragChanged;

    public event EventHandler<TimeSpan>? PositionRequested;
    public event EventHandler<WaveformCueEditEventArgs>? CueEditPreview;
    public event EventHandler<WaveformCueEditEventArgs>? CueEditCommitted;
    public event EventHandler<WaveformNeighborCueEventArgs>? NeighborCueRequested;

    public bool SnapToDetectedSpeech { get; set; } = true;
    public bool PreventNeighborOverlap { get; set; } = true;
    public TimeSpan SnapDistance { get; set; } = TimeSpan.FromMilliseconds(120);

    public WaveformPreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 28, 30);
        ForeColor = Color.Gainsboro;
        MinimumSize = new Size(200, 100);
        Cursor = Cursors.Hand;
    }

    public async Task LoadAsync(string mediaPath, SubtitleCue cue, SubtitleCue? previousCue = null, SubtitleCue? nextCue = null, int cueNumber = 0)
    {
        int version = ++_requestVersion;
        _cueStart = cue.Start;
        _cueEnd = cue.End;
        _previousStart = previousCue?.Start;
        _previousEnd = previousCue?.End;
        _nextStart = nextCue?.Start;
        _nextEnd = nextCue?.End;
        _cueNumber = cueNumber;

        TimeSpan margin = TimeSpan.FromSeconds(2);
        TimeSpan desiredStart = previousCue is null ? cue.Start - margin : previousCue.Start - TimeSpan.FromMilliseconds(350);
        TimeSpan desiredEnd = nextCue is null ? cue.End + margin : nextCue.End + TimeSpan.FromMilliseconds(350);
        _windowStart = desiredStart > TimeSpan.Zero ? desiredStart : TimeSpan.Zero;
        _windowEnd = desiredEnd > cue.End ? desiredEnd : cue.End + margin;
        if (_windowEnd - _windowStart < TimeSpan.FromSeconds(6))
            _windowEnd = _windowStart + TimeSpan.FromSeconds(6);

        _loading = true;
        _error = null;
        _samples = Array.Empty<float>();
        Invalidate();

        try
        {
            float[] samples = await Task.Run(() => DecodeWindow(mediaPath, _windowStart, _windowEnd));
            if (version != _requestVersion) return;
            _samples = samples;
        }
        catch (Exception ex)
        {
            if (version != _requestVersion) return;
            _error = ex.Message;
        }
        finally
        {
            if (version == _requestVersion)
            {
                _loading = false;
                Invalidate();
            }
        }
    }

    public void SetDetectedSpeech(TimeSpan? start, TimeSpan? end)
    {
        _speechStart = start;
        _speechEnd = end;
        Invalidate();
    }

    public void SetPlayhead(TimeSpan position)
    {
        _playhead = position;
        Invalidate();
    }

    public void UpdateCue(TimeSpan start, TimeSpan end)
    {
        _cueStart = start;
        _cueEnd = end;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _windowEnd <= _windowStart || Width <= 1) return;

        // Unutrašnjost vidljivog bloka ima prednost nad tolerancijom ivice
        // susednog bloka, naročito kada je razmak između titlova veoma mali.
        if (IsInsideCue(e.X, _cueStart, _cueEnd) && TryBeginDrag(e.X)) return;

        int neighborOffset = HitNeighborCueInterior(e.X);
        if (neighborOffset == 0 && TryBeginDrag(e.X)) return;
        if (neighborOffset == 0) neighborOffset = HitNeighborCueEdge(e.X);
        if (neighborOffset != 0)
        {
            // Editor sinhrono bira susedni red. LoadAsync odmah postavlja njegova
            // vremena pre prvog await-a, pa isti pokret miša može da nastavi drag.
            NeighborCueRequested?.Invoke(this, new WaveformNeighborCueEventArgs(neighborOffset));
            TryBeginDrag(e.X);
            return;
        }

        PositionRequested?.Invoke(this, XToTime(e.X));
    }

    private bool TryBeginDrag(int x)
    {
        int startX = TimeToX(_cueStart);
        int endX = TimeToX(_cueEnd);
        if (endX < startX) (startX, endX) = (endX, startX);
        const int edgeTolerance = 9;

        if (Math.Abs(x - startX) <= edgeTolerance)
            _dragMode = DragMode.ResizeStart;
        else if (Math.Abs(x - endX) <= edgeTolerance)
            _dragMode = DragMode.ResizeEnd;
        else if (x > startX && x < endX)
            _dragMode = DragMode.Move;
        else
            return false;

        _dragOriginX = x;
        _dragOriginalStart = _cueStart;
        _dragOriginalEnd = _cueEnd;
        _dragChanged = false;
        Capture = true;
        UpdateCursor(x);
        return true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragMode == DragMode.None)
        {
            UpdateCursor(e.X);
            return;
        }

        TimeSpan delta = XDeltaToTime(e.X - _dragOriginX);
        TimeSpan start = _dragOriginalStart;
        TimeSpan end = _dragOriginalEnd;
        TimeSpan minimumDuration = TimeSpan.FromMilliseconds(100);
        TimeSpan leftLimit = PreventNeighborOverlap && _previousEnd is TimeSpan previousEnd ? previousEnd + MinimumCueGap : _windowStart;
        TimeSpan rightLimit = PreventNeighborOverlap && _nextStart is TimeSpan nextStart ? nextStart - MinimumCueGap : _windowEnd;

        switch (_dragMode)
        {
            case DragMode.ResizeStart:
                start = _dragOriginalStart + delta;
                start = Clamp(start, leftLimit, end - minimumDuration);
                if ((ModifierKeys & Keys.Shift) == 0) start = Snap(start, _speechStart);
                break;
            case DragMode.ResizeEnd:
                end = _dragOriginalEnd + delta;
                end = Clamp(end, start + minimumDuration, rightLimit);
                if ((ModifierKeys & Keys.Shift) == 0) end = Snap(end, _speechEnd);
                break;
            case DragMode.Move:
                TimeSpan duration = _dragOriginalEnd - _dragOriginalStart;
                start = _dragOriginalStart + delta;
                end = start + duration;
                if (start < leftLimit)
                {
                    start = leftLimit;
                    end = start + duration;
                }
                if (end > rightLimit)
                {
                    end = rightLimit;
                    start = end - duration;
                }
                if ((ModifierKeys & Keys.Shift) == 0)
                {
                    TimeSpan snappedStart = Snap(start, _speechStart);
                    if (snappedStart != start)
                    {
                        start = snappedStart;
                        end = start + duration;
                    }
                    else
                    {
                        TimeSpan snappedEnd = Snap(end, _speechEnd);
                        if (snappedEnd != end)
                        {
                            end = snappedEnd;
                            start = end - duration;
                        }
                    }
                }
                break;
        }

        // Snapping is evaluated after the initial clamp, so enforce the neighbor
        // limits once more to ensure it cannot remove the required 80 ms gap.
        if (PreventNeighborOverlap)
        {
            if (_dragMode == DragMode.ResizeStart)
                start = Clamp(start, leftLimit, end - minimumDuration);
            else if (_dragMode == DragMode.ResizeEnd)
                end = Clamp(end, start + minimumDuration, rightLimit);
            else if (_dragMode == DragMode.Move)
            {
                TimeSpan duration = end - start;
                if (start < leftLimit)
                {
                    start = leftLimit;
                    end = start + duration;
                }
                if (end > rightLimit)
                {
                    end = rightLimit;
                    start = end - duration;
                }
            }
        }

        if (start < TimeSpan.Zero)
        {
            TimeSpan correction = -start;
            start = TimeSpan.Zero;
            if (_dragMode == DragMode.Move) end += correction;
        }

        _cueStart = start;
        _cueEnd = end;
        _dragChanged = _cueStart != _dragOriginalStart || _cueEnd != _dragOriginalEnd;
        CueEditPreview?.Invoke(this, new WaveformCueEditEventArgs(_cueStart, _cueEnd));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode == DragMode.None) return;

        Capture = false;
        DragMode completedMode = _dragMode;
        _dragMode = DragMode.None;
        UpdateCursor(e.X);

        if (_dragChanged && completedMode != DragMode.None)
            CueEditCommitted?.Invoke(this, new WaveformCueEditEventArgs(_cueStart, _cueEnd));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragMode == DragMode.None) Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        Rectangle r = ClientRectangle;
        if (r.Width < 2 || r.Height < 2) return;

        using var borderPen = new Pen(Color.FromArgb(70, 70, 74));
        g.DrawRectangle(borderPen, 0, 0, r.Width - 1, r.Height - 1);

        if (_loading)
        {
            DrawCentered(g, "Učitavam waveform…", ForeColor);
            return;
        }
        if (!string.IsNullOrWhiteSpace(_error))
        {
            DrawCentered(g, Localization.T("Waveform nije dostupan"), Color.Silver);
            return;
        }
        if (_samples.Length == 0)
        {
            DrawCentered(g, Localization.T("Izaberi titl za grafički prikaz zvuka"), Color.Silver);
            return;
        }

        int mid = r.Height / 2;
        using var centerPen = new Pen(Color.FromArgb(58, 58, 62));
        g.DrawLine(centerPen, 0, mid, r.Width, mid);

        DrawNeighborCue(g, _previousStart, _previousEnd, _cueNumber > 1 ? $"{Localization.T("Prethodni")} #{_cueNumber - 1}" : Localization.T("Prethodni"));
        DrawNeighborCue(g, _nextStart, _nextEnd, _cueNumber > 0 ? $"{Localization.T("Sledeći")} #{_cueNumber + 1}" : Localization.T("Sledeći"));

        if (_speechStart is TimeSpan speechStart && _speechEnd is TimeSpan speechEnd)
        {
            int speechX1 = TimeToX(speechStart);
            int speechX2 = TimeToX(speechEnd);
            if (speechX2 < speechX1) (speechX1, speechX2) = (speechX2, speechX1);
            using var speechBrush = new SolidBrush(Color.FromArgb(62, 55, 190, 105));
            g.FillRectangle(speechBrush, speechX1, 1, Math.Max(2, speechX2 - speechX1), r.Height - 2);
            using var speechPen = new Pen(Color.FromArgb(80, 220, 130), 1.5f);
            g.DrawLine(speechPen, speechX1, 0, speechX1, r.Height);
            g.DrawLine(speechPen, speechX2, 0, speechX2, r.Height);
        }

        int cueX1 = TimeToX(_cueStart);
        int cueX2 = TimeToX(_cueEnd);
        if (cueX2 < cueX1) (cueX1, cueX2) = (cueX2, cueX1);
        using var cueBrush = new SolidBrush(Color.FromArgb(58, 80, 160, 255));
        g.FillRectangle(cueBrush, cueX1, 1, Math.Max(2, cueX2 - cueX1), r.Height - 2);

        using var wavePen = new Pen(Color.FromArgb(215, 225, 235));
        int columns = Math.Min(r.Width, Math.Max(1, _samples.Length / 2));
        for (int x = 0; x < columns; x++)
        {
            int index = (int)((long)x * _samples.Length / columns);
            float min = 0, max = 0;
            int sampleEnd = Math.Min(_samples.Length, index + Math.Max(1, _samples.Length / columns));
            for (int i = index; i < sampleEnd; i++)
            {
                float v = _samples[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            int y1 = mid - (int)(max * (r.Height * 0.43f));
            int y2 = mid - (int)(min * (r.Height * 0.43f));
            g.DrawLine(wavePen, x, y1, x, y2);
        }

        bool overlapsPrevious = _previousEnd is TimeSpan previousBoundary && _cueStart - previousBoundary < MinimumCueGap;
        bool overlapsNext = _nextStart is TimeSpan nextBoundary && nextBoundary - _cueEnd < MinimumCueGap;
        Color cueEdgeColor = overlapsPrevious || overlapsNext ? Color.OrangeRed : Color.FromArgb(90, 170, 255);
        using var cueEdgePen = new Pen(cueEdgeColor, 3);
        g.DrawLine(cueEdgePen, cueX1, 0, cueX1, r.Height);
        g.DrawLine(cueEdgePen, cueX2, 0, cueX2, r.Height);

        using (var cueLabelFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
        using (var cueLabelBrush = new SolidBrush(Color.White))
        {
            string cueLabel = _cueNumber > 0 ? $"{Localization.T("Titl")} #{_cueNumber}" : Localization.T("Izabrani titl");
            SizeF cueLabelSize = g.MeasureString(cueLabel, cueLabelFont);
            float cueLabelX = Math.Max(cueX1 + 4, Math.Min(cueX2 - cueLabelSize.Width - 4, (cueX1 + cueX2 - cueLabelSize.Width) / 2f));
            if (cueX2 - cueX1 > cueLabelSize.Width + 8)
                g.DrawString(cueLabel, cueLabelFont, cueLabelBrush, cueLabelX, 20);
        }

        DrawHandle(g, cueX1, r.Height / 2);
        DrawHandle(g, cueX2, r.Height / 2);

        int playX = TimeToX(_playhead);
        if (playX >= 0 && playX < r.Width)
        {
            using var playPen = new Pen(Color.OrangeRed, 2);
            g.DrawLine(playPen, playX, 0, playX, r.Height);
        }

        using var font = new Font("Segoe UI", 8.5f);
        using var textBrush = new SolidBrush(Color.Silver);
        g.DrawString(Format(_windowStart), font, textBrush, 5, 4);
        string right = Format(_windowEnd);
        SizeF size = g.MeasureString(right, font);
        g.DrawString(right, font, textBrush, r.Width - size.Width - 5, 4);
        string previousGap = _previousEnd is TimeSpan pEnd ? $"{Localization.T("Prethodni razmak:")} {(_cueStart - pEnd).TotalSeconds:0.000}s" : $"{Localization.T("Prethodni:")} —";
        string nextGap = _nextStart is TimeSpan nStart ? $"{Localization.T("Sledeći razmak:")} {(nStart - _cueEnd).TotalSeconds:0.000}s" : $"{Localization.T("Sledeći:")} —";
        string footer = $"{Localization.T("Sivo: klik/prevuci susedni")}   {Localization.T("Plavo: izabrani")}   {Localization.T("Zeleno: govor")}   {previousGap}   {nextGap}";
        g.DrawString(footer, font, textBrush, 5, r.Height - size.Height - 3);
    }

    private void DrawNeighborCue(Graphics g, TimeSpan? start, TimeSpan? end, string label)
    {
        if (start is not TimeSpan cueStart || end is not TimeSpan cueEnd) return;
        int x1 = TimeToX(cueStart);
        int x2 = TimeToX(cueEnd);
        if (x2 < x1) (x1, x2) = (x2, x1);
        if (x2 < 0 || x1 >= Width) return;
        x1 = Math.Max(0, x1);
        x2 = Math.Min(Width - 1, x2);

        using var brush = new SolidBrush(Color.FromArgb(66, 145, 145, 150));
        using var edgePen = new Pen(Color.FromArgb(165, 175, 180), 1.5f);
        g.FillRectangle(brush, x1, 1, Math.Max(2, x2 - x1), Height - 2);
        g.DrawLine(edgePen, x1, 0, x1, Height);
        g.DrawLine(edgePen, x2, 0, x2, Height);

        using var font = new Font("Segoe UI", 8f);
        using var textBrush = new SolidBrush(Color.Gainsboro);
        SizeF labelSize = g.MeasureString(label, font);
        if (x2 - x1 > labelSize.Width + 8)
            g.DrawString(label, font, textBrush, x1 + 4, 4);
    }

    private void DrawHandle(Graphics g, int x, int y)
    {
        Rectangle handle = new(x - 4, y - 13, 8, 26);
        using var brush = new SolidBrush(Color.FromArgb(120, 200, 255));
        using var pen = new Pen(Color.WhiteSmoke);
        g.FillRectangle(brush, handle);
        g.DrawRectangle(pen, handle);
    }

    private void UpdateCursor(int x)
    {
        int startX = TimeToX(_cueStart);
        int endX = TimeToX(_cueEnd);
        if (endX < startX) (startX, endX) = (endX, startX);
        const int edgeTolerance = 9;
        if (Math.Abs(x - startX) <= edgeTolerance || Math.Abs(x - endX) <= edgeTolerance)
            Cursor = Cursors.SizeWE;
        else if (x > startX && x < endX)
            Cursor = Cursors.SizeAll;
        else if (IsNearCueEdge(x, _previousStart, _previousEnd, edgeTolerance) ||
                 IsNearCueEdge(x, _nextStart, _nextEnd, edgeTolerance))
            Cursor = Cursors.SizeWE;
        else if (IsInsideCue(x, _previousStart, _previousEnd) || IsInsideCue(x, _nextStart, _nextEnd))
            Cursor = Cursors.SizeAll;
        else
            Cursor = Cursors.Hand;
    }

    private int HitNeighborCueInterior(int x)
    {
        if (IsInsideCue(x, _previousStart, _previousEnd)) return -1;
        if (IsInsideCue(x, _nextStart, _nextEnd)) return 1;
        return 0;
    }

    private int HitNeighborCueEdge(int x)
    {
        const int edgeTolerance = 9;
        if (IsNearCueEdge(x, _previousStart, _previousEnd, edgeTolerance)) return -1;
        if (IsNearCueEdge(x, _nextStart, _nextEnd, edgeTolerance)) return 1;
        return 0;
    }

    private bool IsInsideCue(int x, TimeSpan? start, TimeSpan? end)
    {
        if (start is not TimeSpan cueStart || end is not TimeSpan cueEnd) return false;
        int x1 = TimeToX(cueStart);
        int x2 = TimeToX(cueEnd);
        if (x2 < x1) (x1, x2) = (x2, x1);
        return x > x1 && x < x2;
    }

    private bool IsNearCueEdge(int x, TimeSpan? start, TimeSpan? end, int tolerance)
    {
        if (start is not TimeSpan cueStart || end is not TimeSpan cueEnd) return false;
        int x1 = TimeToX(cueStart);
        int x2 = TimeToX(cueEnd);
        return Math.Abs(x - x1) <= tolerance || Math.Abs(x - x2) <= tolerance;
    }

    private TimeSpan Snap(TimeSpan value, TimeSpan? target)
    {
        if (!SnapToDetectedSpeech || target is not TimeSpan snapTarget) return value;
        return (value - snapTarget).Duration() <= SnapDistance ? snapTarget : value;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (maximum < minimum) return minimum;
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    private int TimeToX(TimeSpan time)
    {
        if (_windowEnd <= _windowStart) return -1;
        double ratio = (time - _windowStart).TotalMilliseconds / (_windowEnd - _windowStart).TotalMilliseconds;
        return (int)Math.Round(ratio * Math.Max(1, Width - 1));
    }

    private TimeSpan XToTime(int x)
    {
        double ratio = Math.Clamp((double)x / Math.Max(1, Width - 1), 0, 1);
        return _windowStart + TimeSpan.FromTicks((long)((_windowEnd - _windowStart).Ticks * ratio));
    }

    private TimeSpan XDeltaToTime(int deltaX)
    {
        double ratio = (double)deltaX / Math.Max(1, Width - 1);
        return TimeSpan.FromTicks((long)((_windowEnd - _windowStart).Ticks * ratio));
    }

    private void DrawCentered(Graphics g, string text, Color color)
    {
        using var font = new Font("Segoe UI", 10f);
        using var brush = new SolidBrush(color);
        SizeF s = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (Width - s.Width) / 2, (Height - s.Height) / 2);
    }

    private static string Format(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff");

    private static float[] DecodeWindow(string mediaPath, TimeSpan start, TimeSpan end)
    {
        string ffmpeg = Path.Combine(AppContext.BaseDirectory, "runtime", "bin", "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("ffmpeg.exe nije pronađen.", ffmpeg);
        double duration = Math.Max(0.2, (end - start).TotalSeconds);
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-hide_banner -loglevel error -ss {start.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -t {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{mediaPath}\" -vn -ac 1 -ar 8000 -f f32le pipe:1",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process p = Process.Start(psi) ?? throw new InvalidOperationException("FFmpeg nije mogao da se pokrene.");
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        string error = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException("Waveform analiza je prekoračila vreme.");
        }
        if (p.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "FFmpeg greška." : error.Trim());
        byte[] bytes = ms.ToArray();
        int count = bytes.Length / sizeof(float);
        float[] result = new float[count];
        Buffer.BlockCopy(bytes, 0, result, 0, count * sizeof(float));
        float peak = result.Length == 0 ? 1 : result.Max(v => Math.Abs(v));
        if (peak > 0.001f)
            for (int i = 0; i < result.Length; i++) result[i] /= peak;
        return result;
    }
}
