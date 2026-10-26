using SoulsFormats;

namespace BuildCompass.Gen;

/// `textcheck <oldItemBnd> <oldMenuBnd> <outFile>`: diffs the mod's English text against an older
/// build, then writes samples of every language's mod text (UTF-8) for review.
static class TextCheck
{
    static Dictionary<string, FMG> Fmgs(string bnd) => BND4.Read(bnd).Files
        .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name)).ToDictionary(g => g.Key, g => FMG.Read(g.First().Bytes));

    public static void Run(string[] a)
    {
        string mod = Path.Combine(Program.Root, "mod", "msg");
        var lines = new List<string>();
        foreach (var (oldBnd, name, fmg) in new[] { (a[0], "item_dlc02.msgbnd.dcx", "PlaceName"), (a[0], "item_dlc02.msgbnd.dcx", "GoodsInfo"), (a[1], "menu_dlc02.msgbnd.dcx", "EventTextForTalk") })
        {
            var oldE = Fmgs(oldBnd)[fmg].Entries.Where(e => e.ID >= 79_900_000).ToDictionary(e => e.ID, e => e.Text);
            var newE = Fmgs(Path.Combine(mod, "engus", name))[fmg].Entries.Where(e => e.ID >= 79_900_000).ToDictionary(e => e.ID, e => e.Text);
            int diff = oldE.Count(kv => newE.GetValueOrDefault(kv.Key) != kv.Value) + newE.Keys.Count(k => !oldE.ContainsKey(k));
            Console.WriteLine($"engus {fmg}: {oldE.Count} old, {newE.Count} new, {diff} differ");
            foreach (var kv in oldE.Where(kv => newE.GetValueOrDefault(kv.Key) != kv.Value).Take(3))
                Console.WriteLine($"   {kv.Key}: '{kv.Value}' -> '{newE.GetValueOrDefault(kv.Key)}'");
        }
        int[] sampleHover = { 950_000_000, 950_000_001, 950_000_040 };
        foreach (var lang in Directory.GetDirectories(mod).Select(Path.GetFileName))
        {
            var item = Fmgs(Path.Combine(mod, lang, "item_dlc02.msgbnd.dcx"));
            var talk = Fmgs(Path.Combine(mod, lang, "menu_dlc02.msgbnd.dcx"))["EventTextForTalk"];
            var place = item["PlaceName"];
            lines.Add($"== {lang}");
            foreach (var id in sampleHover) lines.Add($"  hover {id}: {place[id]}  [width {Loc.Width(place[id] ?? "")}]");
            var widest = place.Entries.Where(e => e.ID >= 950_000_000).MaxBy(e => Loc.Width(e.Text));
            lines.Add($"  widest hover: {widest.Text}  [width {Loc.Width(widest.Text)}]");
            lines.Add("  menu: " + string.Join(" | ", new[] { 79_900_000, 79_900_100, 79_900_101, 79_900_102, 79_900_103, 79_900_104, 79_900_105, 79_900_106, 79_900_107, 79_900_108, 79_900_001 }.Select(i => talk[i])));
            lines.Add($"  compass: {item["GoodsName"][Builder.CompassGoodsId]} — {item["GoodsInfo"][Builder.CompassGoodsId]}");
        }
        File.WriteAllLines(a[2], lines, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"samples: {a[2]}");
    }
}
