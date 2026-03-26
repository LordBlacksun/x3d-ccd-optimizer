using System.Drawing;
using System.Drawing.Drawing2D;

namespace X3DCcdOptimizer.Tray;

public static class IconGenerator
{
    private static readonly Dictionary<string, Icon> Cache = new();

    public static Icon GetIcon(string colorName)
    {
        if (Cache.TryGetValue(colorName, out var cached))
            return cached;

        var color = colorName switch
        {
            "blue" => Color.FromArgb(75, 158, 255),
            "purple" => Color.FromArgb(155, 109, 255),
            "green" => Color.FromArgb(52, 199, 89),
            "yellow" => Color.FromArgb(255, 179, 64),
            "red" => Color.FromArgb(255, 69, 69),
            _ => Color.FromArgb(75, 158, 255)
        };

        var icon = CreateCircleIcon(color, 32);
        Cache[colorName] = icon;
        return icon;
    }

    private static Icon CreateCircleIcon(Color color, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 2, 2, size - 4, size - 4);

        // Add subtle highlight
        using var highlight = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
        g.FillEllipse(highlight, 6, 4, size / 2, size / 3);

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
