using SoulsFormats;

namespace BuildCompass.Gen;

/// `fmgexact <word>...`: entries in engus menu/item FMGs whose whole text equals a word, with the
/// same id's text in a few other languages, to reuse the game's own translations.
static class FmgExact
{
    public static void Run(string[] words)
    {
        var langs = new[] { "engus", "frafr", "deude", "jpnjp", "zhocn" };
        var fmgs = langs.ToDictionary(l => l, l =>
            new[] { "menu_dlc02.msgbnd.dcx", "item_dlc02.msgbnd.dcx" }
                .SelectMany(b => BND4.Read(Path.Combine(Program.GameDir, "msg", l, b)).Files)
                .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name)).ToDictionary(g => g.Key, g => FMG.Read(g.First().Bytes)));
        foreach (var w in words)
        {
            var hits = fmgs["engus"].SelectMany(kv => kv.Value.Entries.Where(e => e.Text == w).Select(e => (fmg: kv.Key, e.ID))).Take(6).ToList();
            Console.WriteLine($"== {w}: {hits.Count} exact hits");
            foreach (var (fmg, id) in hits)
                Console.WriteLine($"   {fmg} {id}: " + string.Join(" | ", langs.Skip(1).Select(l => $"{l}={fmgs[l].GetValueOrDefault(fmg)?[id]}")));
        }
    }
}
