namespace ChatClient;

internal static class UiTheme
{
    public static readonly Color Canvas = Color.FromArgb(14, 18, 35);
    public static readonly Color Surface = Color.FromArgb(25, 31, 54);
    public static readonly Color SurfaceRaised = Color.FromArgb(38, 46, 76);
    public static readonly Color Primary = Color.FromArgb(116, 103, 240);
    public static readonly Color Mint = Color.FromArgb(74, 222, 193);
    public static readonly Color Text = Color.FromArgb(242, 244, 255);
    public static readonly Color Muted = Color.FromArgb(156, 163, 191);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 19F);
    public static readonly Font SectionFont = new("Segoe UI Semibold", 10F);

    public static void StyleInput(TextBox textBox)
    {
        textBox.BackColor = SurfaceRaised;
        textBox.ForeColor = Text;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Segoe UI", 10F);
        textBox.Margin = new Padding(0, 4, 0, 12);
    }

    public static void StyleButton(Button button, Color background, Color foreground)
    {
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background, .10F);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, .10F);
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI Semibold", 9.5F);
        button.Height = 40;
    }

    public static Label Label(string text, float size = 9F, Color? color = null, FontStyle style = FontStyle.Regular)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = color ?? Muted,
            Font = new Font("Segoe UI", size, style),
            Margin = new Padding(0, 0, 0, 4)
        };
}

internal sealed class CardPanel : Panel
{
    [System.ComponentModel.DefaultValue(14)]
    public int CornerRadius { get; set; } = 14;

    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using var pen = new Pen(Color.FromArgb(55, 255, 255, 255));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        Region = new Region(path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
