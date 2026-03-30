using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace X3DCcdInspector.Tray;

public static class IconGenerator
{
    private static readonly Dictionary<string, Icon> Cache = new();
    private static Icon? _baseAppIcon;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Initialize with the app icon loaded from disk. Call once at startup.
    /// </summary>
    public static void SetBaseIcon(Icon? appIcon)
    {
        _baseAppIcon = appIcon;
    }

    /// <summary>
    /// Returns an icon with a colored status dot composited onto the app icon.
    /// Falls back to a plain colored circle if no base icon is available.
    /// </summary>
    public static Icon GetIcon(string colorName)
    {
        if (Cache.TryGetValue(colorName, out var cached))
            return cached;

        var dotColor = colorName switch
        {
            "blue" => Color.FromArgb(75, 158, 255),
            "purple" => Color.FromArgb(155, 109, 255),
            "green" => Color.FromArgb(52, 199, 89),
            "gray" => Color.FromArgb(128, 128, 128),
            _ => Color.FromArgb(75, 158, 255)
        };

        var icon = _baseAppIcon != null
            ? CompositeIconWithDot(_baseAppIcon, dotColor, 32)
            : CreateCircleIcon(dotColor, 32);

        Cache[colorName] = icon;
        return icon;
    }

    /// <summary>
    /// Composites a small filled circle (status dot) onto the bottom-right corner of the base icon.
    /// Dot is ~25% of icon size with a 1px dark border.
    /// </summary>
    private static Icon CompositeIconWithDot(Icon baseIcon, Color dotColor, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // Draw the base app icon scaled to target size
        g.DrawIcon(baseIcon, new Rectangle(0, 0, size, size));

        // Status dot: ~25% of icon size, bottom-right corner
        int dotSize = size / 4;         // 8px on a 32px icon
        int dotX = size - dotSize - 1;  // 1px margin from right edge
        int dotY = size - dotSize - 1;  // 1px margin from bottom edge

        // Dark border (1px)
        using var borderBrush = new SolidBrush(Color.FromArgb(200, 20, 20, 24));
        g.FillEllipse(borderBrush, dotX - 1, dotY - 1, dotSize + 2, dotSize + 2);

        // Colored dot fill
        using var dotBrush = new SolidBrush(dotColor);
        g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

        var hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    /// <summary>
    /// Fallback: plain colored circle icon (used when no base app icon is available).
    /// </summary>
    private static Icon CreateCircleIcon(Color color, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 2, 2, size - 4, size - 4);

        using var highlight = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
        g.FillEllipse(highlight, 6, 4, size / 2, size / 3);

        var hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }
}
