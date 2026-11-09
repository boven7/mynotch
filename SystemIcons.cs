using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyNotch;

/// <summary>
/// MyNotch's macOS-style system icon set, drawn in code (no Apple/Windows assets).
/// Each icon is drawn on a 256×256 canvas and exported as a multi-size .ico.
/// </summary>
static class SystemIcons
{
    static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>File name → draw function. The names are also used by the registry installer.</summary>
    public static readonly (string Name, Action<DrawingContext> Draw)[] All =
    [
        ("folder", dc => Folder(dc, null)),
        ("desktop", dc => Folder(dc, "")),
        ("documents", dc => Folder(dc, "")),
        ("downloads", dc => Folder(dc, "")),
        ("music", dc => Folder(dc, "")),
        ("pictures", dc => Folder(dc, "")),
        ("videos", dc => Folder(dc, "")),
        ("computer", Computer),
        ("drive", Drive),
        ("network", Network),
        ("trash-empty", dc => Trash(dc, false)),
        ("trash-full", dc => Trash(dc, true)),
        ("explorer", ExplorerApp),
        ("launcher", Launcher),
        // Shell locations
        ("home", dc => Folder(dc, "\uE80F")),
        ("user", dc => Folder(dc, "\uE77B")),
        ("quickaccess", dc => Folder(dc, "\uE734")),
        ("libraries", dc => Folder(dc, "\uE8F1")),
        ("gallery", Gallery),
        ("controlpanel", ControlPanel),
        // Drive
        ("drive-system", dc => { Drive(dc); DrawGlyph(dc, "\uE80F", 40, new Point(70, 124), Color.FromArgb(0x90, 0x50, 0x50, 0x58)); }),
        ("drive-usb", DriveUsb),
        ("drive-network", dc => { Drive(dc); DrawGlyph(dc, "\uE774", 42, new Point(70, 124), Color.FromArgb(0xB0, 0x0A, 0x6F, 0xE0)); }),
        ("disc", Disc),
        // File types
        ("file", dc => Document(dc, null, null)),
        ("file-text", dc => { Document(dc, null, null); TextLines(dc); }),
        ("file-image", dc => Document(dc, "\uEB9F", "#34A853")),
        ("file-audio", dc => Document(dc, "\uEC4F", "#FF2D55")),
        ("file-video", dc => Document(dc, "\uE714", "#AF52DE")),
        ("file-archive", FileArchive),
        ("file-pdf", FilePdf),
        ("app", AppGeneric),
    ];

    // ---------- Drawing ----------

    static Brush V(string top, string bottom) => new LinearGradientBrush(C(top), C(bottom), 90);
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Rect R(double x1, double y1, double x2, double y2) => new(x1, y1, x2 - x1, y2 - y1);

    static void Folder(DrawingContext dc, string? glyph)
    {
        // Back sheet with tab
        var back = new PathGeometry();
        var tab = new StreamGeometry();
        using (var g = tab.Open())
        {
            g.BeginFigure(new Point(16, 66), true, true);
            g.LineTo(new Point(16, 50), true, true);
            g.ArcTo(new Point(30, 36), new Size(14, 14), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(92, 36), true, true);
            g.LineTo(new Point(110, 54), true, true);
            g.LineTo(new Point(110, 66), true, true);
        }
        dc.DrawGeometry(V("#4C9FEC", "#3A8BDC"), null, tab);
        dc.DrawRoundedRectangle(V("#4C9FEC", "#2F7FD3"), null, R(16, 50, 240, 214), 18, 18);
        // Front sheet
        dc.DrawRoundedRectangle(V("#8FCDFB", "#5AAEF3"), null, R(16, 72, 240, 218), 18, 18);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), null, R(24, 74, 232, 77), 1.5, 1.5);
        if (glyph == null) return;
        // Debossed symbol, like macOS folders
        var center = new Point(128, 147);
        DrawGlyph(dc, glyph, 78, center + new Vector(0, 2), Color.FromArgb(0x80, 0xD8, 0xEE, 0xFF));
        DrawGlyph(dc, glyph, 78, center, Color.FromArgb(0xD0, 0x2A, 0x78, 0xC8));
    }

    static void DrawGlyph(DrawingContext dc, string glyph, double size, Point center, Color color)
    {
        var ft = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Glyphs, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size,
            new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    static void Computer(DrawingContext dc)
    {
        // Stand
        var stand = new StreamGeometry();
        using (var g = stand.Open())
        {
            g.BeginFigure(new Point(106, 178), true, true);
            g.LineTo(new Point(150, 178), true, true);
            g.LineTo(new Point(160, 212), true, true);
            g.LineTo(new Point(96, 212), true, true);
        }
        dc.DrawGeometry(V("#C9C9CF", "#A9A9B1"), null, stand);
        dc.DrawRoundedRectangle(V("#E2E2E7", "#BDBDC4"), null, R(72, 208, 184, 220), 6, 6);
        // Screen
        dc.DrawRoundedRectangle(V("#F1F1F4", "#C9C9D0"), new Pen(new SolidColorBrush(C("#9E9EA6")), 2), R(22, 36, 234, 182), 16, 16);
        dc.DrawRoundedRectangle(V("#4F8FE8", "#7B4FD6"), null, R(32, 46, 224, 168), 8, 8);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), null, new Point(96, 120), 70, 36);
    }

    static void Drive(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(V("#F4F4F7", "#C4C4CB"), new Pen(new SolidColorBrush(C("#A0A0A8")), 2), R(22, 84, 234, 188), 24, 24);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), null, R(40, 150, 168, 156), 3, 3);
        dc.DrawEllipse(new SolidColorBrush(C("#34C759")), new Pen(new SolidColorBrush(C("#1E9E44")), 1.5), new Point(204, 153), 7, 7);
    }

    static void Network(DrawingContext dc)
    {
        var c = new Point(128, 128);
        dc.DrawEllipse(V("#5AC8FA", "#0A6FE0"), null, c, 104, 104);
        var line = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), 7);
        dc.DrawEllipse(null, line, c, 44, 104);
        dc.DrawLine(line, new Point(24, 128), new Point(232, 128));
        dc.DrawLine(line, new Point(128, 24), new Point(128, 232));
        dc.DrawLine(line, new Point(44, 82), new Point(212, 82));
        dc.DrawLine(line, new Point(44, 174), new Point(212, 174));
    }

    static void Trash(DrawingContext dc, bool full)
    {
        if (full)
        {
            var paper = new SolidColorBrush(C("#FAFAFA"));
            var edge = new Pen(new SolidColorBrush(C("#C8C8CE")), 2);
            dc.PushTransform(new RotateTransform(-14, 100, 40));
            dc.DrawRoundedRectangle(paper, edge, R(70, 14, 128, 70), 4, 4);
            dc.Pop();
            dc.PushTransform(new RotateTransform(12, 160, 40));
            dc.DrawRoundedRectangle(paper, edge, R(126, 10, 190, 70), 4, 4);
            dc.Pop();
        }
        var body = new StreamGeometry();
        using (var g = body.Open())
        {
            g.BeginFigure(new Point(58, 62), true, true);
            g.LineTo(new Point(198, 62), true, true);
            g.LineTo(new Point(184, 222), true, true);
            g.ArcTo(new Point(170, 236), new Size(14, 14), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(86, 236), true, true);
            g.ArcTo(new Point(72, 222), new Size(14, 14), 0, false, SweepDirection.Clockwise, true, true);
        }
        dc.DrawGeometry(V("#F2F2F5", "#CFCFD6"), new Pen(new SolidColorBrush(C("#A3A3AB")), 3), body);
        var rib = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x60, 0x60, 0x68)), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var (top, bottom) in new[] { (96.0, 100.0), (128.0, 128.0), (160.0, 156.0) })
            dc.DrawLine(rib, new Point(top, 86), new Point(bottom, 214));
        dc.DrawRoundedRectangle(V("#DCDCE1", "#B5B5BD"), new Pen(new SolidColorBrush(C("#9C9CA4")), 2), R(44, 44, 212, 66), 8, 8);
    }

    static void Squircle(DrawingContext dc, Brush fill) => dc.DrawRoundedRectangle(fill, null, R(14, 14, 242, 242), 56, 56);

    public static void ExplorerApp(DrawingContext dc)
    {
        Squircle(dc, V("#66B8FA", "#1F6FD8"));
        dc.PushTransform(new TransformGroup { Children = { new ScaleTransform(0.62, 0.62), new TranslateTransform(48, 52) } });
        var white = new SolidColorBrush(Colors.White);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), null, R(16, 40, 120, 80), 14, 14);
        dc.DrawRoundedRectangle(white, null, R(16, 70, 240, 214), 18, 18);
        dc.Pop();
    }

    public static void Launcher(DrawingContext dc)
    {
        Squircle(dc, V("#8E7CF5", "#4B3FD1"));
        var dot = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                dc.DrawRoundedRectangle(dot, null, R(66 + col * 48, 66 + row * 48, 98 + col * 48, 98 + row * 48), 9, 9);
    }

    // ---------- Documents & extras ----------

    /// <summary>White document sheet with a folded corner; optional colored symbol in the middle.</summary>
    static void Document(DrawingContext dc, string? glyph, string? color)
    {
        var page = new StreamGeometry();
        using (var g = page.Open())
        {
            g.BeginFigure(new Point(64, 18), true, true);
            g.LineTo(new Point(158, 18), true, true);
            g.LineTo(new Point(204, 64), true, true);
            g.LineTo(new Point(204, 226), true, true);
            g.ArcTo(new Point(192, 238), new Size(12, 12), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(64, 238), true, true);
            g.ArcTo(new Point(52, 226), new Size(12, 12), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(new Point(52, 30), true, true);
            g.ArcTo(new Point(64, 18), new Size(12, 12), 0, false, SweepDirection.Clockwise, true, true);
        }
        dc.DrawGeometry(V("#FFFFFF", "#ECECF0"), new Pen(new SolidColorBrush(C("#B9B9C1")), 3), page);
        var fold = new StreamGeometry();
        using (var g = fold.Open())
        {
            g.BeginFigure(new Point(158, 18), true, true);
            g.LineTo(new Point(158, 56), true, true);
            g.ArcTo(new Point(166, 64), new Size(8, 8), 0, false, SweepDirection.Counterclockwise, true, true);
            g.LineTo(new Point(204, 64), true, true);
        }
        dc.DrawGeometry(V("#E4E4EA", "#CFCFD6"), new Pen(new SolidColorBrush(C("#B9B9C1")), 3), fold);
        if (glyph != null) DrawGlyph(dc, glyph, 76, new Point(128, 146), C(color!));
    }

    static void TextLines(DrawingContext dc)
    {
        var pen = new Pen(new SolidColorBrush(C("#A6A6AE")), 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var (y, end) in new[] { (98.0, 176.0), (124.0, 176.0), (150.0, 176.0), (176.0, 176.0), (202.0, 136.0) })
            dc.DrawLine(pen, new Point(80, y), new Point(end, y));
    }

    static void FileArchive(DrawingContext dc)
    {
        Document(dc, null, null);
        var dark = new SolidColorBrush(C("#8E8E96"));
        for (int i = 0; i < 6; i++) dc.DrawRectangle(dark, null, R(i % 2 == 0 ? 118 : 128, 28 + i * 16, i % 2 == 0 ? 128 : 138, 40 + i * 16));
        dc.DrawRoundedRectangle(V("#C8C8CF", "#9C9CA4"), null, R(110, 124, 146, 170), 6, 6);
    }

    static void FilePdf(DrawingContext dc)
    {
        Document(dc, null, null);
        dc.DrawRoundedRectangle(V("#FF5A4F", "#E0281E"), null, R(70, 150, 186, 196), 10, 10);
        var ft = new FormattedText("PDF", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 34, Brushes.White, 1.0);
        dc.DrawText(ft, new Point(128 - ft.Width / 2, 173 - ft.Height / 2));
    }

    static void AppGeneric(DrawingContext dc)
    {
        Squircle(dc, V("#B7B7BF", "#7D7D86"));
        var dot = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
                dc.DrawRoundedRectangle(dot, null, R(74 + col * 60, 74 + row * 60, 122 + col * 60, 122 + row * 60), 12, 12);
    }

    static void Gallery(DrawingContext dc)
    {
        // Landscape: dusk sky, sun, two mountains (original design)
        Squircle(dc, V("#7FC4FF", "#FFB36B"));
        dc.DrawEllipse(new SolidColorBrush(C("#FFF3B0")), null, new Point(170, 92), 28, 28);
        var far = new StreamGeometry();
        using (var g = far.Open())
        {
            g.BeginFigure(new Point(14, 200), true, true);
            g.LineTo(new Point(96, 112), true, true);
            g.LineTo(new Point(176, 200), true, true);
        }
        dc.DrawGeometry(new SolidColorBrush(C("#4E7FB8")), null, far);
        var near = new StreamGeometry();
        using (var g = near.Open())
        {
            g.BeginFigure(new Point(80, 242), true, true);
            g.LineTo(new Point(168, 136), true, true);
            g.LineTo(new Point(242, 214), true, true);
            g.LineTo(new Point(242, 242), true, true);
        }
        dc.PushClip(new RectangleGeometry(R(14, 14, 242, 242), 56, 56));
        dc.DrawGeometry(new SolidColorBrush(C("#2F5D8C")), null, near);
        dc.DrawRectangle(new SolidColorBrush(C("#2F5D8C")), null, R(14, 200, 242, 242));
        dc.Pop();
    }

    static void ControlPanel(DrawingContext dc)
    {
        Squircle(dc, V("#9A9AA2", "#5E5E66"));
        DrawGlyph(dc, "\uE713", 130, new Point(128, 128), Colors.White);
    }

    static void DriveUsb(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(V("#C9C9CF", "#9E9EA6"), null, R(102, 30, 154, 76), 6, 6);
        dc.DrawRectangle(new SolidColorBrush(C("#5E5E66")), null, R(114, 44, 124, 56));
        dc.DrawRectangle(new SolidColorBrush(C("#5E5E66")), null, R(132, 44, 142, 56));
        dc.DrawRoundedRectangle(V("#F4F4F7", "#C4C4CB"), new Pen(new SolidColorBrush(C("#A0A0A8")), 2), R(76, 72, 180, 228), 22, 22);
        dc.DrawEllipse(new SolidColorBrush(C("#0A84FF")), null, new Point(128, 200), 7, 7);
    }

    static void Disc(DrawingContext dc)
    {
        var c = new Point(128, 128);
        dc.DrawEllipse(new RadialGradientBrush(C("#F7F7FA"), C("#BFC6D6")), new Pen(new SolidColorBrush(C("#A0A0A8")), 2), c, 106, 106);
        dc.DrawEllipse(new LinearGradientBrush(Color.FromArgb(0x60, 0xFF, 0x7A, 0xD9), Color.FromArgb(0x60, 0x5A, 0xC8, 0xFA), 45), null, c, 100, 100);
        dc.DrawEllipse(new SolidColorBrush(C("#E9E9EE")), new Pen(new SolidColorBrush(C("#A0A0A8")), 2), c, 34, 34);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x30)), null, c, 12, 12);
    }


    // ---------- Render & export ----------

    public static BitmapSource Render(Action<DrawingContext> draw, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            draw(dc);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static byte[] Png(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Writes an .ico with one PNG entry per size (Vista+ ICO format).</summary>
    public static void WriteIco(string path, Action<DrawingContext> draw)
    {
        var images = IcoSizes.Select(s => (Size: s, Data: Png(Render(draw, s)))).ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count); // ICONDIR
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images) // ICONDIRENTRY
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
    }

    /// <summary>Exports every .ico to the folder; returns the path of the PNG preview.</summary>
    public static string Export(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var (name, draw) in All) WriteIco(Path.Combine(dir, name + ".ico"), draw);

        // Preview: 10-column grid, 96 px icons; each icon row is shown on a light then a dark background
        const int cell = 120, cols = 10;
        int rows = (All.Length + cols - 1) / cols;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int r = 0; r < rows; r++)
            {
                dc.DrawRectangle(new SolidColorBrush(C("#ECECF0")), null, new Rect(0, r * 2 * cell, cell * cols, cell));
                dc.DrawRectangle(new SolidColorBrush(C("#1E1E20")), null, new Rect(0, (r * 2 + 1) * cell, cell * cols, cell));
            }
            for (int i = 0; i < All.Length; i++)
            {
                int r = i / cols, c = i % cols;
                var img = Render(All[i].Draw, 96);
                dc.DrawImage(img, new Rect(c * cell + 12, r * 2 * cell + 12, 96, 96));
                dc.DrawImage(img, new Rect(c * cell + 12, (r * 2 + 1) * cell + 12, 96, 96));
            }
        }
        var sheet = new RenderTargetBitmap(cell * cols, cell * rows * 2, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(dv);
        var preview = Path.Combine(dir, "preview.png");
        File.WriteAllBytes(preview, Png(sheet));
        return preview;
    }
}
