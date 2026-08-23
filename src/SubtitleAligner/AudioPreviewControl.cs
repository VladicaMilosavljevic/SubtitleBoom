using System.Drawing.Drawing2D;

namespace SubtitleAligner;

public sealed class AudioPreviewControl : Control
{
    private string _subtitleText = string.Empty;

    public string SubtitleText
    {
        get => _subtitleText;
        set
        {
            value ??= string.Empty;
            if (_subtitleText == value) return;
            _subtitleText = value;
            Invalidate();
        }
    }

    public AudioPreviewControl()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(24, 24, 24);
        ForeColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        float noteSize = Math.Clamp(Height * 0.28f, 42f, 100f);
        using (var noteFont = new Font("Segoe UI Symbol", noteSize, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var noteBrush = new SolidBrush(Color.FromArgb(185, 205, 225)))
        using (var noteFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            RectangleF noteArea = new(0, Height * 0.08f, Width, Height * 0.50f);
            g.DrawString("♫", noteFont, noteBrush, noteArea, noteFormat);
        }

        if (string.IsNullOrWhiteSpace(_subtitleText)) return;

        float subtitleSize = Math.Clamp(Height * 0.075f, 17f, 30f);
        using var subtitleFont = new Font("Segoe UI", subtitleSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisWord
        };
        RectangleF subtitleArea = new(Width * 0.07f, Height * 0.58f, Width * 0.86f, Height * 0.32f);

        // Jednostavan crni obrub održava čitljivost kao kod video titlova.
        using var outlineBrush = new SolidBrush(Color.Black);
        foreach ((int x, int y) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, 1), (-1, 1), (1, -1) })
        {
            RectangleF shifted = subtitleArea;
            shifted.Offset(x, y);
            g.DrawString(_subtitleText, subtitleFont, outlineBrush, shifted, format);
        }
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(_subtitleText, subtitleFont, textBrush, subtitleArea, format);
    }
}
