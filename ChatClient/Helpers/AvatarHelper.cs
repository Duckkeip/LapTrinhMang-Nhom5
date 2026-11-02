using System.Drawing.Drawing2D;

namespace ChatClient;

internal static class AvatarHelper
{
    public static Bitmap CreateRoundAvatar(string? avatarBase64, string username, int size)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var clipPath = new GraphicsPath();
        clipPath.AddEllipse(new Rectangle(0, 0, size, size));
        graphics.SetClip(clipPath);

        if (!string.IsNullOrWhiteSpace(avatarBase64))
        {
            try
            {
                using var avatarStream = new MemoryStream(Convert.FromBase64String(avatarBase64));
                using var image = Image.FromStream(avatarStream);
                var scaled = ScaleImage(image, size, size);
                graphics.DrawImage(scaled, 0, 0, size, size);
            }
            catch
            {
                DrawFallback(graphics, username, size);
            }
        }
        else
        {
            DrawFallback(graphics, username, size);
        }

        graphics.ResetClip();
        using var border = new Pen(Color.FromArgb(80, 255, 255, 255), 1.5F);
        graphics.DrawEllipse(border, 1, 1, size - 3, size - 3);

        return bitmap;
    }

    private static void DrawFallback(Graphics graphics, string username, int size)
    {
        var background = UiTheme.Primary;
        graphics.Clear(background);

        var initial = string.IsNullOrWhiteSpace(username) ? "?" : username.Trim()[0].ToString().ToUpperInvariant();
        using var font = new Font("Segoe UI Semibold", Math.Max(12F, size * 0.42F), FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        var textSize = graphics.MeasureString(initial, font);
        graphics.DrawString(initial, font, brush, (size - textSize.Width) / 2, (size - textSize.Height) / 2);
    }

    private static Image ScaleImage(Image image, int width, int height)
    {
        var scaled = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(image, new Rectangle(0, 0, width, height));
        return scaled;
    }
}
