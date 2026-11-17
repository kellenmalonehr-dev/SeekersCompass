using SoulsFormats;

namespace GraceLoadouts.Gen;

/// Generates the mod folder: the patched grace talk script and its menu text in every language.
/// Usage: build [--base <mod folder>] [--out <folder>]
static class Builder
{
    // Event flags the menu and grace_loadouts.dll talk through (must match the DLL's lib.rs):
    // 78700+n asks to equip loadout n+1, 78705+n to save it, 78710+n is on while slot n+1 holds one.
    // Unreferenced slots of the 78xxx block in vanilla and Convergence (78800-78809 are taken);
    // Seeker's Compass uses 78900-78999.
    public const uint EquipFlag = 78700, SaveFlag = 78705, StoredFlag = 78710;
    public const int Slots = 5;

    public static void Run(string[] args)
    {
        int bi = Array.IndexOf(args, "--base"), oi = Array.IndexOf(args, "--out");
        if (bi >= 0) Program.Base = args[bi + 1];
        string root = Program.Root, mod = oi >= 0 ? Path.GetFullPath(Path.Combine(root, args[oi + 1])) : Path.Combine(root, "mod");
        Console.WriteLine($"base: {Program.Base ?? "vanilla"} -> {mod}");

        // --- flags
        var gd = GameData.Load(Program.In("regulation.bin"));
        var used = FlagScan.UsedFlags(gd);
        var ours = Enumerable.Range(0, Slots).SelectMany(n => new[] { EquipFlag + (uint)n, SaveFlag + (uint)n, StoredFlag + (uint)n }).ToList();
        var taken = ours.Where(used.Contains).ToList();
        if (taken.Count > 0) throw new Exception($"event flags already in use: {string.Join(",", taken)}");

        // --- text, for every unpacked game language
        var langs = Texts.Languages();
        Console.WriteLine($"languages: {string.Join(" ", langs)}");
        foreach (var lang in langs)
        {
            var talkText = GraceMenu.Texts(new Texts(lang));
            Directory.CreateDirectory(Path.Combine(mod, "msg", lang));
            foreach (var bnd in MenuBnds)
                PatchFmgs(Program.In("msg", lang, bnd), Path.Combine(mod, "msg", lang, bnd), fmgName =>
                    fmgName == "EventTextForTalk" ? talkText : null);
        }

        // --- grace menu (talk script)
        GraceMenu.Build(mod);
    }

    /// The text archives that hold talk-menu lines (EventTextForTalk), per language.
    public static readonly string[] MenuBnds = { "menu.msgbnd.dcx", "menu_dlc01.msgbnd.dcx", "menu_dlc02.msgbnd.dcx" };

    public static void PatchFmgs(string src, string dst, Func<string, Dictionary<int, string>> entriesFor)
    {
        var bnd = BND4.Read(src);
        foreach (var f in bnd.Files)
        {
            var add = entriesFor(Path.GetFileNameWithoutExtension(f.Name));
            if (add == null) continue;
            var fmg = FMG.Read(f.Bytes);
            var existing = fmg.Entries.Select(x => x.ID).ToHashSet();
            if (add.Keys.Any(existing.Contains)) throw new Exception($"text id collision in {f.Name}");
            fmg.Entries.AddRange(add.Select(kv => new FMG.Entry(kv.Key, kv.Value)));
            fmg.Entries = fmg.Entries.OrderBy(x => x.ID).ToList();
            f.Bytes = fmg.Write();
        }
        bnd.Write(dst);
    }
}
