using SoulsFormats;

namespace BuildCompass.Gen;

/// Why do spells/talismans drop out on a base layer? Lists FMG names and samples lots.
static class CnvCheck
{
    public static void Run(string[] args)
    {
        Program.Base = args[0];
        var bnd = BND4.Read(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        Console.WriteLine("item FMGs: " + string.Join(", ", bnd.Files.Select(f => $"{f.ID}:{Path.GetFileNameWithoutExtension(f.Name)}")));
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        var lots = gd["ItemLotParam_map"];
        Console.WriteLine("lot categories: " + string.Join(" ", lots.Rows.GroupBy(r => r.Get<int>("lotItemCategory01")).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        var goods = gd["EquipParamGoods"]; var magic = gd["Magic"];
        int spellGoods = goods.Rows.Count(g => magic[g.ID] != null);
        Console.WriteLine($"goods {goods.Rows.Count}, magic {magic.Rows.Count}, goods with magic row {spellGoods}; goodsType of those: " +
            string.Join(" ", goods.Rows.Where(g => magic[g.ID] != null).GroupBy(g => g.Get<int>("goodsType")).Select(x => $"{x.Key}:{x.Count()}")));
        foreach (var n in new[] { "Glintstone Pebble", "Flame Sling", "Radagon's Soreseal", "Erdtree's Favor" })
        {
            var hit = text.All().FirstOrDefault(x => x.name == n);
            Console.WriteLine($"{n}: {(hit.name == null ? "no name" : $"{hit.fmg} {hit.id}")}");
        }
        var acc = gd["EquipParamAccessory"];
        int accLots = lots.Rows.Count(r => r.Get<int>("lotItemCategory01") == 4);
        Console.WriteLine($"accessory lots {accLots}; e.g. " + string.Join(", ", lots.Rows.Where(r => r.Get<int>("lotItemCategory01") == 4).Take(5).Select(r => $"{r.ID}:{r["lotItemId01"].Value}='{text.Get("AccessoryName", r.Get<int>("lotItemId01"))}' row={(acc[r.Get<int>("lotItemId01")] != null)}")));
        var spellLots = lots.Rows.Where(r => r.Get<int>("lotItemCategory01") == 1 && magic[r.Get<int>("lotItemId01")] != null).Take(5);
        Console.WriteLine("spell lots: " + string.Join(", ", spellLots.Select(r => $"{r.ID}:{r["lotItemId01"].Value}='{text.Get("GoodsName", r.Get<int>("lotItemId01"))}' type={goods[r.Get<int>("lotItemId01")]?["goodsType"].Value}")));
    }
}

static class CnvSpells
{
    /// Where do the base layer's spell lots get referenced from?
    public static void Run(string[] args)
    {
        Program.Base = args[0];
        var gd = GameData.Load(Program.In("regulation.bin"));
        var magic = gd["Magic"]; var goods = gd["EquipParamGoods"]; var lots = gd["ItemLotParam_map"];
        bool IsSpell(int id) => magic[id] != null && goods[id]?.Get<int>("goodsType") is 5 or 16 or 17 or 18;
        var spellLots = lots.Rows.Where(r => Enumerable.Range(1, 8).Any(s => r.Get<int>($"lotItemCategory0{s}") == 1 && IsSpell(r.Get<int>($"lotItemId0{s}")))).Select(r => r.ID).ToHashSet();
        Console.WriteLine($"map lots containing a spell: {spellLots.Count}; id range {spellLots.Min()}..{spellLots.Max()}; sample {string.Join(",", spellLots.OrderBy(x => x).Take(12))}");
        int treasureHits = 0, treasures = 0;
        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            var msb = MSBE.Read(path);
            foreach (var t in msb.Events.Treasures) { treasures++; if (Enumerable.Range(0, 10).Any(o => spellLots.Contains(t.ItemLotID + o))) treasureHits++; }
        }
        Console.WriteLine($"MSB treasures {treasures}, referencing a spell lot {treasureHits}");
        var shops = gd["ShopLineupParam"].Rows.Count(r => r.Get<int>("equipType") == 3 && IsSpell(r.Get<int>("equipId")));
        Console.WriteLine($"shop rows selling spells: {shops}");
    }
}

static class FindLot
{
    /// Which events (in base + vanilla) mention a value; prints the event's instructions.
    public static void Run(string[] args)
    {
        Program.Base = args[0];
        uint v = uint.Parse(args[1]);
        int shown = 0;
        foreach (var path in Program.InFiles("*.emevd.dcx", "event"))
        {
            var emevd = EMEVD.Read(path);
            foreach (var ev in emevd.Events)
            {
                var hits = ev.Instructions.Where(i => Enumerable.Range(0, Math.Max(0, i.ArgData.Length / 4)).Any(k => BitConverter.ToUInt32(i.ArgData, k * 4) == v)).ToList();
                if (hits.Count == 0 || shown++ > 3) continue;
                Console.WriteLine($"{Path.GetFileName(path)} event {ev.ID} params={ev.Parameters.Count}:");
                foreach (var ins in ev.Instructions.Take(30)) Console.WriteLine($"   {(hits.Contains(ins) ? ">" : " ")}{ins.Bank}[{ins.ID}] {BitConverter.ToString(ins.ArgData)}");
            }
        }
        Console.WriteLine($"events mentioning {v}: {shown}");
    }
}

static class TalkGrep
{
    /// `talkgrep [--base dir] <int>`: talk-script commands whose args contain the literal.
    public static void Run(string[] args)
    {
        int bi = Array.IndexOf(args, "--base");
        if (bi >= 0) { Program.Base = args[bi + 1]; args = args.Where((a, i) => i != bi && i != bi + 1).ToArray(); }
        int v = int.Parse(args[0]);
        var lit = EzState.Lit(v);
        foreach (var path in Program.InFiles("*.talkesdbnd.dcx", "script", "talk"))
            foreach (var f in BND4.Read(path).Files.Where(f => f.Name.EndsWith(".esd")))
                foreach (var (g, states) in ESD.Read(f.Bytes).StateGroups)
                    foreach (var (sid, s) in states)
                        foreach (var c in s.EntryCommands.Where(c => c.Arguments.Any(a => a.SequenceEqual(lit))))
                            Console.WriteLine($"{(path.StartsWith(Program.GameDir) ? "vanilla" : "base")} {Path.GetFileName(path)} {Path.GetFileName(f.Name)} g{g} s{sid}: c{c.CommandBank}_{c.CommandID}({string.Join(", ", c.Arguments.Select(EzState.Disasm))})");
    }
}
