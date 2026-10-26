using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Xml.Linq;
using SoulsFormats;

namespace BuildCompass.Gen;

/// Repaints unused world-map icon slots with item icons. The map GFX imports every MENU_MAP_N
/// sprite by name, so new numbers would need GFX edits; instead we reuse slots nothing places
/// (no param references them) and point their layout entries at new art in free atlas space.
/// Art reuses icon 80's frame (a framed square) with our own glyph inside.
static class Icons
{
    // Which unused MENU_MAP slot (and the atlas it lives in) each kind takes over. Slots in the 80s
    // are the small-marker range (NPC marks are 80) and are being tested for drawing above location
    // art; 212/233 are DLC location slots, which drew underneath it.
    // scale: how much bigger to draw than the shared size, because the map GFX shrinks some slots more
    // than others (86 rendered ~30% smaller than 89/233 in-game at the same pixel size).
    static readonly Dictionary<Kind, (ushort id, string atlas, float scale)> Slots = new()
    {
        [Kind.Weapon] = (86, "SB_MapCursor_02", 1.45f),
        [Kind.Spell] = (89, "SB_MapCursor_02", 1f),
        [Kind.Armor] = (214, "SB_MapCursor_03_dlc", 1f), // 212 never drew in-game; 214 did
        [Kind.Talisman] = (233, "SB_MapCursor_03_dlc", 1f),
    };
    public static readonly Dictionary<Kind, ushort> Slot = Slots.ToDictionary(kv => kv.Key, kv => kv.Value.id);

    const string FrameAtlas = "SB_MapCursor_02";
    static readonly string Texconv = Path.Combine(Program.Root, "tools", "texconv", "texconv.exe");

    public static void Build(string mod)
    {
        foreach (var res in new[] { "hi", "low" })
        {
            // Each file from the base layer if it has one (Convergence ships menu/hi but not menu/low).
            string work = Path.Combine(Program.Root, "work", "icons", res);
            Directory.CreateDirectory(work);

            var tpf = TPF.Read(Program.In("menu", res, "01_common.tpf.dcx"));
            var lyt = BND4.Read(Program.In("menu", res, "01_common.sblytbnd.dcx"));
            var atlasNames = Slots.Values.Select(v => v.atlas).Append(FrameAtlas).Distinct().ToList();
            var layoutFiles = atlasNames.ToDictionary(n => n, n => lyt.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == n));
            var layouts = atlasNames.ToDictionary(n => n, n => XDocument.Parse(Encoding.UTF8.GetString(layoutFiles[n].Bytes)));
            var bitmaps = atlasNames.ToDictionary(n => n, n => ToBitmap(tpf, n, work));
            var occupied = atlasNames.ToDictionary(n => n, n => layouts[n].Root.Elements("SubTexture").Select(e => Rect(e)).ToList());

            var frameAtlas = bitmaps[FrameAtlas];
            var r80 = Rect(layouts[FrameAtlas], "MENU_MAP_80.png");
            using var frame = EmptyFrame(frameAtlas, r80); // copied before any atlas is painted
            var colors = SampleColors(frameAtlas, r80);
            // One icon size for all kinds: icon 80's size, or smaller if a slot is narrower (89 is), so nothing clips.
            int size = Slots.Values.Select(v => Rect(layouts[v.atlas], $"MENU_MAP_{v.id}.png")).Select(r => Math.Min(r.Width, r.Height)).Append(frame.Width).Min();
            using (var sheet = new Bitmap(frame.Width * 4, frame.Height)) // preview of the four icons
            using (var gs = Graphics.FromImage(sheet))
            {
                int i = 0;
                foreach (var (kind, (slot, atlasName, scale)) in Slots)
                {
                    var atlas = bitmaps[atlasName];
                    var entries = layouts[atlasName].Root.Elements("SubTexture").ToList();
                    var entry = entries.First(e => (string)e.Attribute("name") == $"MENU_MAP_{slot}.png");
                    var old = Rect(entry);
                    // Same canvas size as the slot's original sprite (the GFX scales to it), icon centered.
                    using var canvas = new Bitmap(old.Width, old.Height, PixelFormat.Format32bppArgb);
                    int drawn = Math.Min((int)(size * scale), Math.Min(old.Width, old.Height));
                    using (var g = Graphics.FromImage(canvas))
                    {
                        int x = (old.Width - drawn) / 2, y = (old.Height - drawn) / 2;
                        string art = Path.Combine(Program.Root, "data", "icons", kind.ToString().ToLowerInvariant() + ".png");
                        if (File.Exists(art))
                        {
                            // Custom art replaces frame + glyph, at icon 80's size so it matches vanilla marks.
                            using var custom = LoadArt(art);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(custom, new Rectangle(x, y, drawn, drawn));
                        }
                        else
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(frame, new Rectangle(x, y, drawn, drawn));
                            DrawGlyph(g, kind, new RectangleF(x, y, drawn, drawn), colors);
                        }
                    }
                    // An unused slot with its own region is painted in place; one sharing its region
                    // with another sprite (214/215 point at 213's art) gets fresh space instead.
                    bool shared = entries.Any(e => e != entry && Rect(e) == old);
                    var spot = shared ? FindFree(atlas, occupied[atlasName], old.Size) : old.Location;
                    Blit(atlas, canvas, spot);
                    occupied[atlasName].Add(new Rectangle(spot, old.Size));
                    entry.SetAttributeValue("x", spot.X);
                    entry.SetAttributeValue("y", spot.Y);
                    gs.DrawImage(canvas, new Rectangle(i++ * frame.Width, 0, frame.Width, frame.Height),
                        new Rectangle((old.Width - drawn) / 2, (old.Height - drawn) / 2, drawn, drawn), GraphicsUnit.Pixel);
                    Console.WriteLine($"  {res}: {kind} -> MENU_MAP_{slot} in {atlasName} at {spot.X},{spot.Y} ({old.Width}x{old.Height}{(shared ? ", moved" : ", in place")})");
                }
                sheet.Save(Path.Combine(Program.Root, "work", "icons", $"preview_{res}.png"));
            }

            foreach (var name in Slots.Values.Select(v => v.atlas).Distinct())
            {
                FromBitmap(tpf, name, bitmaps[name], work);
                layoutFiles[name].Bytes = Encoding.UTF8.GetBytes(layouts[name].ToString(SaveOptions.None));
            }
            foreach (var b in bitmaps.Values) b.Dispose();
            AddItemIcon(tpf, lyt, res, work);
            string outDir = Path.Combine(mod, "menu", res);
            Directory.CreateDirectory(outDir);
            tpf.Write(Path.Combine(outDir, "01_common.tpf.dcx"));
            lyt.Write(Path.Combine(outDir, "01_common.sblytbnd.dcx"));
        }
    }

    // --- the compass's own inventory icon ------------------------------------

    /// New item icon id for the Seeker's Compass (vanilla's highest is 40824). Inventory and shop
    /// lists draw MENU_ItemIcon_<id> from the SB_Icon atlases. The large picture of the full
    /// description view lives in menu/hi/00_solo (a 1.4 GB archive) and is not replaced.
    public const int CompassIconId = 40900;

    /// Paints the compass icon into free space in an SB_Icon atlas and adds its layout entry.
    /// Art: data/icons/compass.png if present, else the world map's compass rose (MENU_MAP_Bearing).
    static void AddItemIcon(TPF tpf, BND4 lyt, string res, string work)
    {
        var iconLayouts = lyt.Files.Where(f => Path.GetFileNameWithoutExtension(f.Name).StartsWith("SB_Icon"))
            .OrderByDescending(f => f.Name.Contains("_dlc")).ToList(); // DLC atlases are the emptiest
        string name = $"MENU_ItemIcon_{CompassIconId:00000}.png";
        if (iconLayouts.Any(f => Encoding.UTF8.GetString(f.Bytes).Contains(name))) throw new Exception("item icon id collision");

        // Size and half flag follow this resolution's existing item icons (160 hi, 80 low).
        var sample = XDocument.Parse(Encoding.UTF8.GetString(iconLayouts[0].Bytes)).Root.Elements("SubTexture").First();
        int size = (int)sample.Attribute("width");
        string half = (string)sample.Attribute("half");

        using var art = CompassArt(tpf, lyt, work, size);
        foreach (var file in iconLayouts)
        {
            string atlasName = Path.GetFileNameWithoutExtension(file.Name);
            var layout = XDocument.Parse(Encoding.UTF8.GetString(file.Bytes));
            var occupied = layout.Root.Elements("SubTexture").Select(e => Rect(e)).ToList();
            var atlas = ToBitmap(tpf, atlasName, work);
            Point spot;
            try { spot = FindFree(atlas, occupied, new Size(size, size), step: 16); }
            catch (Exception) { atlas.Dispose(); continue; } // full; try the next atlas
            Blit(atlas, art, spot);
            layout.Root.Add(new XElement("SubTexture", new XAttribute("name", name), new XAttribute("x", spot.X), new XAttribute("y", spot.Y),
                new XAttribute("width", size), new XAttribute("height", size), new XAttribute("half", half)));
            FromBitmap(tpf, atlasName, atlas, work);
            file.Bytes = Encoding.UTF8.GetBytes(layout.ToString(SaveOptions.None));
            atlas.Dispose();
            art.Save(Path.Combine(Program.Root, "work", "icons", $"compass_{res}.png"));
            Console.WriteLine($"  {res}: compass item icon {CompassIconId} in {atlasName} at {spot.X},{spot.Y} ({size}px)");
            return;
        }
        throw new Exception("no SB_Icon atlas has room for the compass icon");
    }

    static Bitmap CompassArt(TPF tpf, BND4 lyt, string work, int size)
    {
        var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        string custom = Path.Combine(Program.Root, "data", "icons", "compass.png");
        if (File.Exists(custom))
        {
            using var art = LoadArt(custom);
            g.DrawImage(art, new Rectangle(0, 0, size, size));
            return canvas;
        }
        var layout = XDocument.Parse(Encoding.UTF8.GetString(lyt.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "SB_MapCursor").Bytes));
        var r = Rect(layout, "MENU_MAP_Bearing.png");
        using var atlas = ToBitmap(tpf, "SB_MapCursor", work);
        // Fit the rose (taller than wide) into the square with a little margin, centered.
        float scale = size * 0.92f / Math.Max(r.Width, r.Height);
        int w = (int)(r.Width * scale), h = (int)(r.Height * scale);
        g.DrawImage(atlas, new Rectangle((size - w) / 2, (size - h) / 2, w, h), r, GraphicsUnit.Pixel);
        return canvas;
    }

    // --- layout / atlas helpers ---------------------------------------------

    static Rectangle Rect(XElement e) => new((int)e.Attribute("x"), (int)e.Attribute("y"), (int)e.Attribute("width"), (int)e.Attribute("height"));
    static Rectangle Rect(XDocument d, string name) => Rect(d.Root.Elements("SubTexture").First(e => (string)e.Attribute("name") == name));

    /// First spot (scanning right column first) not overlapping any sprite and fully transparent.
    static Point FindFree(Bitmap atlas, List<Rectangle> occupied, Size size, int step = 2)
    {
        const int pad = 4;
        for (int x = atlas.Width - size.Width - pad; x >= pad; x -= step)
            for (int y = pad; y + size.Height + pad <= atlas.Height; y += step)
            {
                var r = new Rectangle(x - pad, y - pad, size.Width + 2 * pad, size.Height + 2 * pad);
                if (occupied.Any(o => o.IntersectsWith(r))) continue;
                if (Transparent(atlas, r)) return new Point(x, y);
            }
        throw new Exception($"no free {size} in atlas");
    }

    static bool Transparent(Bitmap b, Rectangle r)
    {
        for (int y = r.Top; y < r.Bottom; y += 2)
            for (int x = r.Left; x < r.Right; x += 2)
                if (b.GetPixel(x, y).A > 8) return false;
        return true;
    }

    static void Blit(Bitmap dst, Bitmap src, Point at)
    {
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.SetPixel(at.X + x, at.Y + y, src.GetPixel(x, y)); // overwrite, keep exact alpha
    }

    /// Icon 80 with the hooded figure cleared: keeps its frame and outer glow.
    static Bitmap EmptyFrame(Bitmap frameAtlas, Rectangle r80)
    {
        var b = frameAtlas.Clone(r80, PixelFormat.Format32bppArgb);
        // The figure overlaps the bottom edge, so rebuild that edge from the (clean) top edge.
        for (int y = (int)(b.Height * 0.72f); y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                b.SetPixel(x, y, b.GetPixel(x, b.Height - 1 - y));
        int lo = (int)(b.Width * 0.19f), hi = (int)(b.Width * 0.81f);
        for (int y = lo; y < hi; y++)
            for (int x = lo; x < hi; x++)
                b.SetPixel(x, y, Color.Transparent);
        return b;
    }

    /// Fill = the figure's tan, ink = the darkest opaque pixel of the frame.
    static (Color fill, Color ink) SampleColors(Bitmap frameAtlas, Rectangle r80)
    {
        var fill = frameAtlas.GetPixel(r80.X + r80.Width / 2, r80.Y + (int)(r80.Height * 0.72f));
        Color ink = Color.Black; float best = 1;
        for (int x = r80.X; x < r80.Right; x++)
        {
            var c = frameAtlas.GetPixel(x, r80.Y + r80.Height / 2);
            if (c.A > 240 && c.GetBrightness() < best) { best = c.GetBrightness(); ink = c; }
        }
        return (Color.FromArgb(255, fill), ink);
    }

    /// Loads user art. Image generators often bake in a flat background instead of real
    /// transparency, so if there is no alpha, pixels close to the corner color are keyed out.
    static Bitmap LoadArt(string path)
    {
        using var file = new Bitmap(path);
        var b = new Bitmap(file.Width, file.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b)) g.DrawImage(file, 0, 0, file.Width, file.Height);
        bool hasAlpha = false;
        for (int y = 0; y < b.Height && !hasAlpha; y += 4)
            for (int x = 0; x < b.Width && !hasAlpha; x += 4)
                hasAlpha = b.GetPixel(x, y).A < 250;
        if (!hasAlpha) KeyOutBackground(b);
        return Normalize(b);
    }

    /// Trims to the visible art and re-centers it on a square canvas with fixed padding, so
    /// icons whose frames were drawn at different sizes or offsets line up as a set.
    static Bitmap Normalize(Bitmap b)
    {
        int minX = b.Width, minY = b.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A > 40) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        if (maxX < 0) return b;
        var art = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        int side = (int)(Math.Max(art.Width, art.Height) / 0.88f); // 6% padding each side for the glow
        var sq = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sq))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Stretch the frame to a square so every icon's border lands in the same place.
            int inner = (int)(side * 0.88f), off = (side - inner) / 2;
            g.DrawImage(b, new Rectangle(off, off, inner, inner), art, GraphicsUnit.Pixel);
        }
        b.Dispose();
        return sq;
    }

    /// Flood-fills the background from the image border, so only background connected to the edge
    /// is removed; bright highlights inside the art (same color as the background) are kept.
    static void KeyOutBackground(Bitmap b)
    {
        var bg = b.GetPixel(0, 0);
        int Dist(Color c) => Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
        var seen = new bool[b.Width, b.Height];
        var queue = new Queue<Point>();
        void Seed(int x, int y) { if (!seen[x, y] && Dist(b.GetPixel(x, y)) < 80) { seen[x, y] = true; queue.Enqueue(new Point(x, y)); } }
        for (int x = 0; x < b.Width; x++) { Seed(x, 0); Seed(x, b.Height - 1); }
        for (int y = 0; y < b.Height; y++) { Seed(0, y); Seed(b.Width - 1, y); }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var c = b.GetPixel(p.X, p.Y);
            int d = Dist(c);
            // Pure background goes fully transparent; the anti-aliased fringe fades out.
            b.SetPixel(p.X, p.Y, d < 40 ? Color.Transparent : Color.FromArgb((d - 40) * 255 / 40, c));
            if (d >= 40) continue; // don't spread past the fringe into the art
            if (p.X > 0) Seed(p.X - 1, p.Y);
            if (p.Y > 0) Seed(p.X, p.Y - 1);
            if (p.X < b.Width - 1) Seed(p.X + 1, p.Y);
            if (p.Y < b.Height - 1) Seed(p.X, p.Y + 1);
        }
    }

    // --- glyphs ---------------------------------------------------------------

    /// Draws a glyph in a 100x100 design space mapped onto the frame's interior.
    static void DrawGlyph(Graphics g, Kind kind, RectangleF frame, (Color fill, Color ink) c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float inner = frame.Width * 0.66f;
        var box = new RectangleF(frame.X + (frame.Width - inner) / 2, frame.Y + (frame.Height - inner) / 2, inner, inner);
        var state = g.Save();
        g.TranslateTransform(box.X, box.Y);
        g.ScaleTransform(box.Width / 100f, box.Height / 100f);
        using var fill = new SolidBrush(c.fill);
        using var ink = new Pen(c.ink, 6f) { LineJoin = LineJoin.Round };
        using var inkFill = new SolidBrush(c.ink);
        using var thin = new Pen(c.ink, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        void Shape(GraphicsPath p) { g.FillPath(fill, p); g.DrawPath(ink, p); }

        switch (kind)
        {
            case Kind.Weapon: // sword, point up-right
                g.TranslateTransform(50, 50); g.RotateTransform(45); g.TranslateTransform(-50, -50);
                using (var blade = new GraphicsPath())
                {
                    blade.AddPolygon(new PointF[] { new(50, 0), new(58, 10), new(58, 64), new(42, 64), new(42, 10) });
                    Shape(blade);
                }
                g.DrawLine(thin, 50, 12, 50, 60);
                using (var guard = new GraphicsPath()) { guard.AddRectangle(new RectangleF(28, 63, 44, 9)); Shape(guard); }
                using (var grip = new GraphicsPath()) { grip.AddRectangle(new RectangleF(45, 72, 10, 17)); Shape(grip); }
                using (var pommel = new GraphicsPath()) { pommel.AddEllipse(43, 88, 14, 12); Shape(pommel); }
                break;

            case Kind.Spell: // scroll
                using (var body = new GraphicsPath()) { body.AddRectangle(new RectangleF(22, 20, 56, 60)); Shape(body); }
                using (var top = new GraphicsPath()) { top.AddEllipse(12, 10, 76, 18); Shape(top); }
                using (var bottom = new GraphicsPath()) { bottom.AddEllipse(12, 72, 76, 18); Shape(bottom); }
                foreach (float y in new[] { 38f, 48f, 58f }) g.DrawLine(thin, 32, y, 68, y);
                break;

            case Kind.Armor: // great helm
                using (var helm = new GraphicsPath())
                {
                    helm.AddBezier(22, 50, 22, 10, 78, 10, 78, 50);
                    helm.AddLine(78, 50, 74, 92);
                    helm.AddLine(74, 92, 26, 92);
                    helm.CloseFigure();
                    Shape(helm);
                }
                g.FillRectangle(inkFill, 28, 46, 44, 8);   // eye slit
                g.DrawLine(thin, 50, 60, 50, 86);         // breath line
                foreach (float x in new[] { 38f, 62f }) g.FillEllipse(inkFill, x - 3, 70, 6, 6);
                break;

            case Kind.Talisman: // pendant on a chain
                g.DrawLines(new Pen(c.ink, 5f) { LineJoin = LineJoin.Round }, new PointF[] { new(26, 40), new(50, 6), new(74, 40) });
                using (var disc = new GraphicsPath()) { disc.AddEllipse(22, 36, 56, 56); Shape(disc); }
                using (var gem = new GraphicsPath()) { gem.AddEllipse(38, 52, 24, 24); g.FillPath(inkFill, gem); }
                g.FillEllipse(fill, 44, 57, 7, 7); // highlight
                break;
        }
        g.Restore(state);
    }

    // --- texture conversion ------------------------------------------------------

    static Bitmap ToBitmap(TPF tpf, string name, string work)
    {
        var tex = tpf.Textures.First(t => t.Name == name);
        string dds = Path.Combine(work, name + ".dds");
        File.WriteAllBytes(dds, tex.Headerize());
        RunTexconv("-ft", "png", "--ignore-srgb", "-y", "-o", work, dds);
        // Load via a copy so the PNG file isn't locked.
        using var fromFile = new Bitmap(Path.Combine(work, name + ".png"));
        return new Bitmap(fromFile);
    }

    static void FromBitmap(TPF tpf, string name, Bitmap bmp, string work)
    {
        string png = Path.Combine(work, name + ".png");
        bmp.Save(png, ImageFormat.Png);
        // UI atlases are single-mip BC7, straight alpha. --ignore-srgb: the PNG carries an sRGB tag,
        // and without this texconv converts to linear, darkening every sprite in the atlas.
        RunTexconv("-f", "BC7_UNORM", "-m", "1", "--ignore-srgb", "-y", "-o", work, png);
        tpf.Textures.First(t => t.Name == name).Bytes = File.ReadAllBytes(Path.Combine(work, name + ".dds"));
    }

    static void RunTexconv(params string[] args)
    {
        var psi = new ProcessStartInfo(Texconv) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-nologo");
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception("texconv failed: " + output);
    }
}
