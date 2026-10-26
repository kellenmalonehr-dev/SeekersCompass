using SoulsFormats;

namespace BuildCompass.Gen;

static class Program
{
    /// Elden Ring's Game folder: BUILDCOMPASS_GAME, else found through Steam's library list.
    /// Its map/event/msg/menu/script folders must be unpacked (e.g. with UXM Selective Unpack).
    public static readonly string GameDir = Environment.GetEnvironmentVariable("BUILDCOMPASS_GAME") ?? Steam.FindGameDir()
        ?? throw new Exception("Elden Ring not found; set BUILDCOMPASS_GAME to its ...\\ELDEN RING\\Game folder.");
    public static readonly string Root = FindRoot();

    /// Optional base layer (another overhaul's mod folder, e.g. Convergence). When set, every input
    /// file the base provides replaces vanilla's, so the generated files are merged on top of it.
    public static string Base;

    /// An input file: from the base layer if it has one, else vanilla.
    public static string In(params string[] parts)
    {
        if (Base != null)
        {
            string b = Path.Combine(new[] { Base }.Concat(parts).ToArray());
            if (File.Exists(b)) return b;
        }
        return Path.Combine(new[] { GameDir }.Concat(parts).ToArray());
    }

    /// All input files in a folder: vanilla's, with the base layer's versions (and additions) winning.
    public static IEnumerable<string> InFiles(string pattern, params string[] dir)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { GameDir, Base })
        {
            if (root == null) continue;
            string d = Path.Combine(new[] { root }.Concat(dir).ToArray());
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.GetFiles(d, pattern)) files[Path.GetFileName(f)] = f;
        }
        return files.Values.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
    }

    static int Main(string[] args)
    {
        // SoulsFormats needs Oodle 8+ for DCX (ER's own oo2core_6 lacks the exports it probes), so
        // borrow a newer copy: BUILDCOMPASS_OODLE, else any oo2core_8/9 shipped by an installed Steam game.
        if (!Directory.GetFiles(AppContext.BaseDirectory, "oo2core_*_win64.dll").Any(f => !f.Contains("oo2core_6")))
        {
            string src = Environment.GetEnvironmentVariable("BUILDCOMPASS_OODLE") ?? Steam.FindOodle()
                ?? throw new Exception("No oo2core_8/9_win64.dll found; set BUILDCOMPASS_OODLE to one (many Unreal/Steam games ship it).");
            File.Copy(src, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(src)));
        }

        string cmd = args.Length > 0 ? args[0] : "build";
        switch (cmd)
        {
            case "explore": Explore.Run(args.Skip(1).ToArray()); return 0;
            case "dump": Dump.Run(args.Skip(1).ToArray()); return 0;
            case "flags": FlagScan.Report(args.Skip(1).ToArray()); return 0;
            case "build": Builder.Run(args.Skip(1).ToArray()); return 0;
            case "verify": Explore.Verify(args.Length > 1 ? args[1] : "mod"); return 0;
            case "dialogs": Explore.Dialogs(); return 0;
            case "iconuse": Explore.IconUse(args.Skip(1).ToArray()); return 0;
            case "shop": Explore.Shop(args.Skip(1).ToArray()); return 0;
            case "instr": Explore.Instr(args.Skip(1).ToArray()); return 0;
            case "npcs": NpcOffsets.Run(); return 0;
            case "spelltypes": Explore.SpellTypes(); return 0;
            case "talk": TalkDump.Run(args.Skip(1).ToArray()); return 0;
            case "fmgfind": FmgFind.Run(args.Skip(1).ToArray()); return 0;
            case "esdcheck": EsdCheck.Run(); return 0;
            case "esdcompare": EsdCompare.Run(); return 0;
            case "armorbonus": ArmorBonus.Run(); return 0;
            case "namefind": NameFind.Run(args.Skip(1).ToArray()); return 0;
            case "solo": Solo.Run(args.Skip(1).ToArray()); return 0;
            case "cnvcheck": CnvCheck.Run(args.Skip(1).ToArray()); return 0;
            case "cnvspells": CnvSpells.Run(args.Skip(1).ToArray()); return 0;
            case "findlot": FindLot.Run(args.Skip(1).ToArray()); return 0;
            case "talkgrep": TalkGrep.Run(args.Skip(1).ToArray()); return 0;
            case "tpf": Tpf.List(args.Skip(1).ToArray()); return 0;
            case "layouts": Tpf.Layouts(args.Skip(1).ToArray()); return 0;
            case "export": Tpf.Export(args.Skip(1).ToArray()); return 0;
            // Release zips: .NET (Core) always writes forward-slash entries, unlike Windows PowerShell 5.
            case "zip": if (File.Exists(args[2])) File.Delete(args[2]); System.IO.Compression.ZipFile.CreateFromDirectory(args[1], args[2], System.IO.Compression.CompressionLevel.Optimal, false); return 0;
            default: Console.Error.WriteLine($"unknown command {cmd}"); return 1;
        }
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new Exception("BuildCompass root not found");
    }
}
