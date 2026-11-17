using SoulsFormats;

namespace GraceItems.Gen;

/// Generates the camp menu: the patched grace talk script and its text in every language.
/// Usage: build [--base <mod folder>] [--out <folder>]
static class Builder
{
    // Event flags the menu and the DLL talk through; see DESIGN.md. Must match the DLL's menu.rs.
    public const uint AliveFlag = 78715, RequestFlag = 78716, IndexBitsFlag = 78717, OwnedFlag = 78725, EquippedFlag = 78810;
    public const int IndexBits = 7, MaxItems = 64;
    // Found-item pop-ups and the status lines, in the ends of the owned and equipped ranges freed by
    // capping items at 64 (78800-78809 and 78900+ belong to others).
    public const uint FoundPendingFlag = 78789, FoundAckFlag = 78790, FoundIndexFlag = 78791, MinutesFlag = 78874, KillsFlag = 78881;
    public const int NumberBits = 7;
    /// The found index that means "items from graces found before the mod".
    public const int WelcomeIndex = 127;
    /// Status numbers top out here: minutes left, and kills ("100+").
    public const int MaxMinutes = 120, MaxKills = 100;

    public static void Run(string[] args)
    {
        int bi = Array.IndexOf(args, "--base"), oi = Array.IndexOf(args, "--out");
        if (bi >= 0) Program.Base = args[bi + 1];
        string root = Program.Root, mod = oi >= 0 ? Path.GetFullPath(Path.Combine(root, args[oi + 1])) : Path.Combine(root, "menu");
        Console.WriteLine($"base: {Program.Base ?? "vanilla"} -> {mod}");

        var items = Item.Load(Path.Combine(root, "data", "grace_items.txt"));
        if (items.Count > MaxItems) throw new Exception($"{items.Count} items; the menu's flags hold {MaxItems}");
        Console.WriteLine($"{items.Count} items");

        // --- flags
        var gd = GameData.Load(Program.In("regulation.bin"));
        var used = FlagScan.UsedFlags(gd);
        var ours = new List<uint> { AliveFlag, RequestFlag };
        for (int b = 0; b < IndexBits; b++) ours.Add(IndexBitsFlag + (uint)b);
        for (int n = 0; n < MaxItems; n++) { ours.Add(OwnedFlag + (uint)n); ours.Add(EquippedFlag + (uint)n); }
        ours.Add(FoundPendingFlag); ours.Add(FoundAckFlag);
        foreach (var start in new[] { FoundIndexFlag, MinutesFlag, KillsFlag })
            for (int b = 0; b < NumberBits; b++) ours.Add(start + (uint)b);
        if (ours.Count != ours.Distinct().Count()) throw new Exception("two of our flags overlap");
        var taken = ours.Where(used.Contains).ToList();
        if (taken.Count > 0) throw new Exception($"event flags already in use: {string.Join(",", taken)}");

        // --- text, for every unpacked game language
        var langs = Texts.Languages();
        Console.WriteLine($"languages: {string.Join(" ", langs)}");
        foreach (var lang in langs)
        {
            var talkText = CampMenu.Texts(new Texts(lang), items);
            Directory.CreateDirectory(Path.Combine(mod, "msg", lang));
            foreach (var bnd in MenuBnds)
                PatchFmgs(Program.In("msg", lang, bnd), Path.Combine(mod, "msg", lang, bnd), fmgName =>
                    fmgName == "EventTextForTalk" ? talkText : null);
        }

        // --- grace menu (talk script)
        CampMenu.Build(mod, items);
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
