using SoulsFormats;

namespace BuildCompass.Gen;

/// `fmgfind <fmg> <text...>`: ids in a menu FMG whose text contains the query.
static class FmgFind
{
    public static void Run(string[] args)
    {
        var bnd = BND4.Read(Path.Combine(Program.GameDir, "msg", "engus", "menu_dlc02.msgbnd.dcx"));
        string q = string.Join(" ", args.Skip(1));
        foreach (var f in bnd.Files.Where(f => Path.GetFileNameWithoutExtension(f.Name).StartsWith(args[0])))
            foreach (var e in FMG.Read(f.Bytes).Entries.Where(e => e.Text != null && e.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(15))
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(f.Name)} {e.ID}: {e.Text.Replace("\n", " | ")}");
    }
}
