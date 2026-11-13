using SoulsFormats;

namespace BuildCompass.Gen;

/// Merchants hand over their bell bearing the first time the player comes near them, so the Twin
/// Maiden Husks can sell their stock without the merchant having to die.
///
/// A merchant is any character whose death drop (NpcParam itemLotId_map) holds a "... Merchant's
/// Bell Bearing" or Kalé's. One event per merchant in common.emevd waits for the player to come
/// within VisitRadius and awards that same drop. The drop's own pickup flag makes it a one-time
/// gift: once it is set the event ends at once, and the merchant drops nothing on death.
/// Named characters with bell bearings (Sellen, Gostoc, Patches...) are left alone: theirs are
/// tied to their quests.
static class BellBearings
{
    const float VisitRadius = 5;
    const uint Player = 10000;
    // The event's id: the first of these that the game (and the base layer) doesn't use.
    const long FirstEventId = 9005790;

    record Merchant(uint Entity, int Lot, uint Flag, string Bell, string Where);

    static bool IsMerchantBell(string name) =>
        name != null && name.Contains("Bell Bearing") && (name.Contains("Merchant's") || name.StartsWith("Kal"));

    static List<Merchant> Find(GameData gd, Text text)
    {
        var lots = gd["ItemLotParam_map"];
        var npcs = gd["NpcParam"];
        // The bell bearing in a drop, which may sit in a later lot of the same chain.
        (int lot, string bell)? BellIn(int first)
        {
            for (int id = first; id < first + 10 && lots[id] != null; id++)
                for (int s = 1; s <= 8; s++)
                    if (lots[id].Get<int>($"lotItemCategory0{s}") == 1 && text.Get("GoodsName", lots[id].Get<int>($"lotItemId0{s}")) is string name && IsMerchantBell(name))
                        return (id, name);
            return null;
        }
        var bellOf = npcs.Rows.Select(n => (n.ID, drop: n.Get<int>("itemLotId_map"))).Where(n => n.drop > 0)
            .Select(n => (n.ID, n.drop, bell: BellIn(n.drop))).Where(n => n.bell != null).ToDictionary(n => n.ID, n => (n.drop, n.bell.Value));

        var found = new Dictionary<uint, Merchant>();
        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            string map = Path.GetFileName(path).Split('.')[0];
            if (map.EndsWith("_99")) continue;
            foreach (var e in MSBE.Read(path).Parts.Enemies)
            {
                if (!bellOf.TryGetValue(e.NPCParamID, out var b)) continue;
                if (e.EntityID == 0) { Console.WriteLine($"bell bearings: {map} {e.Name} ({b.Value.bell}) has no entity id; skipped"); continue; }
                uint flag = lots[b.Value.lot].Get<uint>("getItemFlagId");
                if (flag == 0) throw new Exception($"item lot {b.Value.lot} ({b.Value.bell}) has no pickup flag, so it would be given again and again");
                // The drop is awarded from its first lot, as the game does on death.
                found.TryAdd(e.EntityID, new Merchant(e.EntityID, b.drop, flag, b.Value.bell, $"{map} {e.Name}"));
            }
        }
        return found.Values.OrderBy(m => m.Bell).ThenBy(m => m.Entity).ToList();
    }

    /// Writes event/common.emevd.dcx into the mod folder.
    public static void Build(string mod, GameData gd, Text text)
    {
        var merchants = Find(gd, text);
        var emedf = new Emedf(Path.Combine(Program.Root, "deps", "DarkScript3", "DarkScript3", "Resources", "er-common.emedf.json"));
        var emevd = EMEVD.Read(Program.In("event", "common.emevd.dcx"));
        // Guard against the library rewriting the file differently from how the game stores it.
        if (!DCX.Decompress(emevd.Write()).ToArray().AsSpan().SequenceEqual(DCX.Decompress(File.ReadAllBytes(Program.In("event", "common.emevd.dcx"))).ToArray()))
            throw new Exception("common.emevd.dcx does not survive a read and write unchanged");
        long id = FirstEventId;
        while (emevd.Events.Any(e => e.ID == id)) id++;

        // Started with (merchant entity, item lot, the lot's pickup flag).
        var ev = new EMEVD.Event(id, EMEVD.Event.RestBehaviorType.Default);
        ev.Instructions.Add(emedf.Make(1003, 2, 0, 1, 0, 0u));                       // END IF Event Flag: end, on, flag
        ev.Instructions.Add(emedf.Make(3, 3, 0, 1, Player, 0u, VisitRadius, 1));     // IF Entity In Radius Of Entity: main, inside, player, merchant
        ev.Instructions.Add(emedf.Make(2003, 4, 0));                                 // Award Item Lot
        ev.Parameters.Add(new EMEVD.Parameter(0, 4, 8, 4));
        ev.Parameters.Add(new EMEVD.Parameter(1, 8, 0, 4));
        ev.Parameters.Add(new EMEVD.Parameter(2, 0, 4, 4));
        emevd.Events.Add(ev);

        var init = emevd.Events.First(e => e.ID == 0).Instructions;
        for (int i = 0; i < merchants.Count; i++)
        {
            var m = merchants[i];
            init.Add(emedf.Make(2000, 0, i, (uint)id, m.Entity, (uint)m.Lot, m.Flag)); // Initialize Event
            Console.WriteLine($"  {m.Bell,-40} {m.Where,-28} entity {m.Entity} lot {m.Lot} flag {m.Flag}");
        }
        string path = Path.Combine(mod, "event", "common.emevd.dcx");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        emevd.Write(path);
        Console.WriteLine($"bell bearings: {merchants.Count} merchants give theirs on a first visit (event {id})");
    }
}
