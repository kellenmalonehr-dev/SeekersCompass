using SoulsFormats;

namespace BuildCompass.Gen;

/// Inspection helpers for menu textures (TPF) and sprite layouts (sblytbnd).
static class Tpf
{
    public static void List(string[] args)
    {
        foreach (var path in args)
        {
            var tpf = TPF.Read(path);
            Console.WriteLine($"{Path.GetFileName(path)}: {tpf.Textures.Count} textures");
            foreach (var t in tpf.Textures)
                Console.WriteLine($"  {t.Name} fmt={t.Format} {t.Bytes.Length / 1024}KB");
        }
    }

    /// `layouts <sblytbnd> [filter]`: lists layout files, prints those matching the filter.
    public static void Layouts(string[] args)
    {
        var bnd = BND4.Read(args[0]);
        foreach (var f in bnd.Files)
        {
            Console.WriteLine($"{f.ID} {f.Name} {f.Bytes.Length}B");
            if (args.Length > 1 && f.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase))
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(f.Bytes));
        }
    }

    /// `export <tpf> <texture> <outDir>`: writes one texture's DDS for texconv.
    public static void Export(string[] args)
    {
        var tex = TPF.Read(args[0]).Textures.First(t => t.Name == args[1]);
        Directory.CreateDirectory(args[2]);
        File.WriteAllBytes(Path.Combine(args[2], tex.Name + ".dds"), tex.Headerize());
    }
}
