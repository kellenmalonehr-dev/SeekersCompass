using SoulsFormats;

namespace BuildCompass.Gen;

/// Lists 00_solo (BXF4 of TPFs) entries matching a filter.
static class Solo
{
    public static void Run(string[] args)
    {
        string dir = Path.Combine(Program.GameDir, "menu", "hi");
        var bxf = BXF4.Read(Path.Combine(dir, "00_solo.tpfbhd"), Path.Combine(dir, "00_solo.tpfbdt"));
        Console.WriteLine($"{bxf.Files.Count} files");
        foreach (var f in bxf.Files.Where(f => args.Length == 0 || f.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).Take(15))
        {
            var tpf = TPF.Read(f.Bytes);
            Console.WriteLine($"{f.ID} {f.Name} {f.Bytes.Length / 1024}KB: " + string.Join(", ", tpf.Textures.Select(t => $"{t.Name} fmt{t.Format}")));
        }
    }
}
