using System.Text.RegularExpressions;
using SoulsFormats;

namespace BuildCompass.Gen;

/// The Twin Maiden Husks' "Purchase" opens one shop holding everything they sell: their own stock
/// and the stock of every bell bearing handed to them, where the game has a separate menu line
/// and shop for each bell bearing.
///
/// In their talk script (t600001110) each bell bearing line is shown while a flag is on (the bell
/// bearing was handed in) and leads to a state that opens a block of ShopLineupParam rows. A
/// shop is one unbroken range of rows, and the merchants' own shops open the very same blocks, so
/// the blocks can't be gated or moved. Instead every block is copied into one new range, each
/// copied row released by its block's flag, and Purchase opens that range. A copy keeps its
/// row's stock flag, so what is bought in one shop is sold out in the other.
/// The separate bell bearing lines stay in the menu and work as before.
static class OneShop
{
    const int OpenShop = 22;
    const string Script = "t600001110.esd";

    record Block(uint Flag, int First, int Last);

    public static void Apply(BND4 holdTalk, PARAM shops)
    {
        var file = holdTalk.Files.First(f => f.Name.EndsWith(Script));
        var esd = ESD.Read(file.Bytes);

        // The Husks' own stock: the one shop no flagged menu line leads to.
        var blocks = new List<Block>();
        ESD.CommandCall purchase = null;
        foreach (var group in esd.StateGroups.Values)
        {
            // Menu index -> the flag its line is shown by (0 for a line that is always there).
            var flagOf = new Dictionary<int, uint>();
            foreach (var c in group.Values.SelectMany(s => s.EntryCommands).Where(c => c.CommandBank is 1 or 5 && c.CommandID is 19 or 149))
            {
                bool conditional = c.CommandBank == 5;
                if (!int.TryParse(EzState.Disasm(c.Arguments[conditional ? 1 : 0]).Split(' ')[0], out int index)) continue;
                var m = conditional ? Regex.Match(EzState.Disasm(c.Arguments[0]), @"^15 (\d+) call1 1 ==") : Match.Empty;
                flagOf.TryAdd(index, m.Success ? uint.Parse(m.Groups[1].Value) : 0);
            }
            // Where each index leads: through the plain "go on" states to the first shop.
            foreach (var cond in group.Values.SelectMany(s => s.Conditions))
            {
                var m = Regex.Match(EzState.Disasm(cond.Evaluator), @"^(?:23 call0 set_r0|get_r0|23 call0) (\d+) == end$");
                if (!m.Success || cond.TargetState == null || !flagOf.TryGetValue(int.Parse(m.Groups[1].Value), out uint flag)) continue;
                var shop = FirstShop(group, cond.TargetState.Value);
                if (shop == null) continue;
                int first = int.Parse(EzState.Disasm(shop.Arguments[0]).Split(' ')[0]), last = int.Parse(EzState.Disasm(shop.Arguments[1]).Split(' ')[0]);
                if (flag == 0) { if (purchase != null) throw new Exception("the Husks' script has more than one shop that is always open"); purchase = shop; }
                if (!blocks.Any(b => b.First == first)) blocks.Add(new Block(flag, first, last));
            }
        }
        if (purchase == null) throw new Exception("the Husks' own shop wasn't found in their script");

        // One new unbroken range: the Husks' own stock first, then each bell bearing's.
        var ordered = blocks.OrderBy(b => b.Flag != 0).ThenBy(b => b.First).ToList();
        int count = ordered.Sum(b => shops.Rows.Count(r => r.ID >= b.First && r.ID <= b.Last));
        int start = 1_000_000;
        while (shops.Rows.Any(r => r.ID >= start && r.ID < start + count)) start += 100_000;
        int next = start, alreadyGated = 0;
        foreach (var b in ordered)
            foreach (var row in shops.Rows.Where(r => r.ID >= b.First && r.ID <= b.Last).OrderBy(r => r.ID).ToList())
            {
                var copy = new PARAM.Row(row) { ID = next++, Name = "" };
                if (b.Flag != 0)
                {
                    if (copy.Get<uint>("eventFlag_forRelease") == 0) copy.Set("eventFlag_forRelease", b.Flag);
                    else alreadyGated++; // the game releases this item by its own flag; that stays
                }
                shops.Rows.Add(copy);
            }
        shops.Rows = shops.Rows.OrderBy(r => r.ID).ToList();

        purchase.Arguments[0] = EzState.Lit(start);
        purchase.Arguments[1] = EzState.Lit(next - 1);
        file.Bytes = esd.Write();
        Console.WriteLine($"one shop: {ordered.Count} blocks ({ordered.Count(b => b.Flag != 0)} bell bearings), {count} rows copied to {start}-{next - 1}; {alreadyGated} rows keep the game's own release flag");
    }

    /// The shop a menu choice opens: the first state with an open-shop command reached without
    /// passing through a state that builds or shows a menu.
    static ESD.CommandCall FirstShop(Dictionary<long, ESD.State> group, long from)
    {
        var seen = new HashSet<long>();
        var queue = new Queue<long>(new[] { from });
        while (queue.Count > 0)
        {
            long id = queue.Dequeue();
            if (!seen.Add(id) || !group.TryGetValue(id, out var s)) continue;
            var shop = s.EntryCommands.FirstOrDefault(c => c.CommandBank == 1 && c.CommandID == OpenShop);
            if (shop != null) return shop;
            if (s.EntryCommands.Any(c => c.CommandBank is 1 or 5 && c.CommandID is 10 or 19 or 20 or 149 or 6) || s.EntryCommands.Any(c => c.CommandBank == 6)) continue;
            foreach (var c in s.Conditions) Follow(c, queue);
        }
        return null;
    }

    static void Follow(ESD.Condition c, Queue<long> queue)
    {
        if (c.TargetState != null) queue.Enqueue(c.TargetState.Value);
        foreach (var sub in c.Subconditions) Follow(sub, queue);
    }
}
