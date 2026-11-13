namespace GraceLoadouts.Gen;

/// What this part of the generator (once the separate GraceLoadouts generator) sees as its inputs: the mod
/// folder being built first, then whatever the main build reads (its base layer, then the game).
static class Program
{
    /// The mod folder being built; this part patches the files the earlier parts wrote there.
    public static string Base;
    public static string GameDir => BuildCompass.Gen.Program.GameDir;
    public static string Root => BuildCompass.Gen.Program.Root;
    public static string Deps => Path.Combine(Root, "deps");

    public static string In(params string[] parts)
    {
        if (Base != null)
        {
            string b = Path.Combine(new[] { Base }.Concat(parts).ToArray());
            if (File.Exists(b)) return b;
        }
        return BuildCompass.Gen.Program.In(parts);
    }

    public static IEnumerable<string> InFiles(string pattern, params string[] dir)
    {
        var files = BuildCompass.Gen.Program.InFiles(pattern, dir).ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
        if (Base != null)
        {
            string d = Path.Combine(new[] { Base }.Concat(dir).ToArray());
            if (Directory.Exists(d))
                foreach (var f in Directory.GetFiles(d, pattern)) files[Path.GetFileName(f)] = f;
        }
        return files.Values.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
    }
}
