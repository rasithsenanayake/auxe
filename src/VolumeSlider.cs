using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed class VolumeSlider : Control
{
    readonly Func<int, Task> write;
    int level;
    int? pending;
    bool writing;
    bool dragging;

    public VolumeSlider(int initial, Func<int, Task> write)
    {
        this.write = write;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        SetLevel(initial);
    }

    public void ReadLevel(int value)
    {
        if (!dragging && !writing && level != value) SetLevel(value);
    }

    void SetLevel(int value)
    {
        level = Math.Max(0, Math.Min(100, value));
        AccessibleDescription = level + " percent";
        Invalidate();
    }

    async void Commit()
    {
        pending = level;
        if (writing) return;
        writing = true;
        try
        {
            while (pending.HasValue)
            {
                int value = pending.Value;
                pending = null;
                await write(value);
            }
        }
        finally { writing = false; }
    }

    int TrackStart { get { return Math.Max(6, Height / 4); } }
    int TrackEnd { get { return Width - Height * 2; } }

    void MoveThumb(int x)
    {
        int width = Math.Max(1, TrackEnd - TrackStart);
        SetLevel((int)Math.Round((x - TrackStart) * 100d / width));
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button != MouseButtons.Left || args.X > TrackEnd + TrackStart) return;
        Focus();
        dragging = true;
        Capture = true;
        MoveThumb(args.X);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (dragging) MoveThumb(args.X);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (!dragging || args.Button != MouseButtons.Left) return;
        MoveThumb(args.X);
        Capture = false; // Losing capture commits the latest position.
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (!dragging || Capture) return;
        dragging = false;
        Commit();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
            case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                return true;
            default: return base.IsInputKey(keyData);
        }
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        int next;
        switch (args.KeyCode)
        {
            case Keys.Left: case Keys.Down: next = level - 1; break;
            case Keys.Right: case Keys.Up: next = level + 1; break;
            case Keys.PageDown: next = level - 10; break;
            case Keys.PageUp: next = level + 10; break;
            case Keys.Home: next = 0; break;
            case Keys.End: next = 100; break;
            default: return;
        }
        args.Handled = true;
        SetLevel(next);
        Commit();
    }

    protected override void OnGotFocus(EventArgs args) { base.OnGotFocus(args); Invalidate(); }
    protected override void OnLostFocus(EventArgs args) { base.OnLostFocus(args); Invalidate(); }

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.Clear(Theme.Card);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float y = Height / 2f;
        float thumb = TrackStart + (TrackEnd - TrackStart) * level / 100f;
        float scale = Height / 26f;
        float size = 6 * scale;
        using (var track = new Pen(Theme.Border, 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var fill = new Pen(Theme.Accent, 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var dot = new SolidBrush(Theme.Ink))
        using (var outline = new Pen(Theme.Accent, 1.25f))
        using (var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
        {
            graphics.DrawLine(track, TrackStart, y, TrackEnd, y);
            if (level > 0) graphics.DrawLine(fill, TrackStart, y, thumb, y);
            graphics.FillEllipse(dot, thumb - size, y - size, size * 2, size * 2);
            if (Focused) graphics.DrawEllipse(outline, thumb - size - 2, y - size - 2, size * 2 + 4, size * 2 + 4);
            graphics.DrawString(level + "%", Font, dot, new RectangleF(TrackEnd + size + 4, 0, Width - TrackEnd - size - 4, Height), format);
        }
    }
}
