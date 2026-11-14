using SoulsFormats;

namespace BuildCompass.Gen;

/// Sellers hand over their bell bearing the first time the player opens their shop, so the Twin
/// Maiden Husks can sell their stock without the seller having to die.
///
/// A seller is any character whose death drop (NpcParam itemLotId_map) holds a bell bearing: the
/// wandering merchants and the named characters who keep a shop. In each of their talk scripts,
/// every state that opens a shop (c1_22) gets two states in front of it: the first goes straight
/// on if the drop's pickup flag is already set, the second awards the drop (c1_104) and waits the
/// way the game's own give-an-item helper does. The pickup flag makes it a one-time gift, and
/// the seller then drops nothing on death.
static class BellBearings
{
    const int OpenShop = 22, ClearMenu = 20, AwardLot = 104, GetFlag = 15;

    record Seller(int TalkId, int Lot, uint Flag, string Bell, string Where);

    static List<Seller> Find(GameData gd, Text text)
    {
        var lots = gd["ItemLotParam_map"];
        // The bell bearing in a drop, which may sit in a later lot of the same chain.
        (int lot, string bell)? BellIn(int first)
        {
            for (int id = first; id < first + 10 && lots[id] != null; id++)
                for (int s = 1; s <= 8; s++)
                    if (lots[id].Get<int>($"lotItemCategory0{s}") == 1 && text.Get("GoodsName", lots[id].Get<int>($"lotItemId0{s}")) is string name
                        && name.Contains("Bell Bearing") && !name.StartsWith("[ERROR]"))
                        return (id, name);
            return null;
        }
        var bellOf = gd["NpcParam"].Rows.Select(n => (n.ID, drop: n.Get<int>("itemLotId_map"))).Where(n => n.drop > 0)
            .Select(n => (n.ID, n.drop, bell: BellIn(n.drop))).Where(n => n.bell != null).ToDictionary(n => n.ID, n => (n.drop, n.bell.Value));

        var found = new Dictionary<int, Seller>();
        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            string map = Path.GetFileName(path).Split('.')[0];
            if (map.EndsWith("_99")) continue;
            foreach (var e in MSBE.Read(path).Parts.Enemies)
            {
                if (e.TalkID <= 0 || !bellOf.TryGetValue(e.NPCParamID, out var b)) continue;
                uint flag = lots[b.Value.lot].Get<uint>("getItemFlagId");
                if (flag == 0) throw new Exception($"item lot {b.Value.lot} ({b.Value.bell}) has no pickup flag, so it would be given again and again");
                // The drop is awarded from its first lot, as the game does on death.
                found.TryAdd(e.TalkID, new Seller(e.TalkID, b.drop, flag, b.Value.bell, $"{map} {e.Name}"));
            }
        }
        return found.Values.OrderBy(m => m.Bell).ThenBy(m => m.TalkId).ToList();
    }

    static byte[] FlagIsOn(uint flag)
    {
        var b = new List<byte>(EzState.Call(GetFlag, (int)flag));
        EzState.Int(b, 1);
        b.Add(0x95); // ==
        return EzState.End(b);
    }

    static void Retarget(ESD.Condition c, long from, long to)
    {
        if (c.TargetState == from) c.TargetState = to;
        foreach (var sub in c.Subconditions) Retarget(sub, from, to);
    }

    /// Writes the sellers' talk files into the mod folder. Must run before anything else adds to
    /// those files (the hubs do): it starts each from the game's (or the base layer's) copy.
    public static void Build(string mod, GameData gd, Text text)
    {
        string talkDir = Path.Combine(mod, "script", "talk");
        // Leftovers of an earlier build; the grace menu's file was written fresh just before.
        if (Directory.Exists(talkDir))
            foreach (var f in Directory.GetFiles(talkDir, "*.talkesdbnd.dcx").Where(f => !Path.GetFileName(f).StartsWith("m00_00_00_00")))
                File.Delete(f);

        var sellers = Find(gd, text).ToDictionary(s => $"t{s.TalkId}.esd");
        var always = EzState.Lit(1);
        byte[] wait = null; // how the game's own helper waits after giving an item
        var files = Program.InFiles("*.talkesdbnd.dcx", "script", "talk").ToList();
        foreach (var path in files)
        {
            if (wait != null) break;
            foreach (var f in BND4.Read(path).Files.Where(f => f.Name.EndsWith(".esd")))
            {
                var helper = ESD.Read(f.Bytes).StateGroups.Values.SelectMany(g => g.Values)
                    .FirstOrDefault(s => s.EntryCommands.Count == 1 && s.EntryCommands[0].CommandBank == 1 && s.EntryCommands[0].CommandID == AwardLot && s.Conditions.Count == 1);
                if (helper != null) { wait = helper.Conditions[0].Evaluator; break; }
            }
        }
        if (wait == null) throw new Exception("the game's give-an-item helper wasn't found in any talk script");

        var done = new HashSet<string>();
        foreach (var path in files)
        {
            var bnd = BND4.Read(path);
            bool changed = false;
            foreach (var f in bnd.Files)
            {
                if (!sellers.TryGetValue(Path.GetFileName(f.Name), out var seller)) continue;
                var esd = ESD.Read(f.Bytes);
                // The library may lay a script out differently from the game's own file (the grace menu
                // script is rewritten the same way), but a second pass must change nothing more.
                byte[] once = esd.Write();
                if (!ESD.Read(once).Write().AsSpan().SequenceEqual(once)) throw new Exception($"{f.Name} is not stable when read and written");
                int shops = 0;
                // Only scripts that sell something. In those, the gift comes before the talk menu is built (so on
                // the first conversation, without choosing anything) and, for a seller with no menu, before the shop.
                if (!esd.StateGroups.Values.Any(g => g.Values.Any(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == OpenShop)))) continue;
                foreach (var group in esd.StateGroups.Values)
                    foreach (long shop in group.Where(s => s.Value.EntryCommands.Any(c => c.CommandBank == 1 && (c.CommandID == OpenShop || c.CommandID == ClearMenu))).Select(s => s.Key).ToList())
                    {
                        if (shop == 0) throw new Exception($"{f.Name}: a shop opens in a group's first state, which can't be given a state in front");
                        long check = group.Keys.Max() + 1, give = check + 1;
                        foreach (var s in group.Values) foreach (var c in s.Conditions) Retarget(c, shop, check);
                        group[check] = new ESD.State
                        {
                            Conditions =
                            {
                                new ESD.Condition { TargetState = shop, Evaluator = FlagIsOn(seller.Flag) },
                                new ESD.Condition { TargetState = give, Evaluator = always },
                            },
                        };
                        group[give] = new ESD.State
                        {
                            EntryCommands = { new ESD.CommandCall(1, AwardLot, EzState.Lit(seller.Lot)) },
                            Conditions = { new ESD.Condition { TargetState = shop, Evaluator = wait } },
                        };
                        shops++;
                    }
                if (shops == 0) continue;
                f.Bytes = esd.Write();
                changed = true;
                done.Add(Path.GetFileName(f.Name));
                Console.WriteLine($"  {seller.Bell,-40} {Path.GetFileName(path).Split('.')[0]} {Path.GetFileName(f.Name)}, {shops} menu and shop state{(shops > 1 ? "s" : "")}; lot {seller.Lot} flag {seller.Flag} ({seller.Where})");
            }
            if (!changed) continue;
            Directory.CreateDirectory(talkDir);
            bnd.Write(Path.Combine(talkDir, Path.GetFileName(path)));
        }
        foreach (var s in sellers.Where(s => !done.Contains(s.Key)))
            Console.WriteLine($"  {s.Value.Bell}: {s.Key} ({s.Value.Where}) opens no shop; left alone");
        Console.WriteLine($"bell bearings: {done.Count} talk scripts give theirs the first time their menu comes up");
    }
}
