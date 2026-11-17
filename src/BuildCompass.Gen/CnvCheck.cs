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

static class TalkCmd
{
    /// `talkcmd <bank> <id> [max]`: where talk scripts use a command, with its arguments.
    /// `talkcmd` alone: every command in use, with how often.
    public static void Run(string[] args)
    {
        var counts = new Dictionary<(int, int), int>();
        int shown = 0, max = args.Length > 2 ? int.Parse(args[2]) : 12;
        foreach (var path in Program.InFiles("*.talkesdbnd.dcx", "script", "talk"))
            foreach (var f in BND4.Read(path).Files.Where(f => f.Name.EndsWith(".esd")))
                foreach (var (g, states) in ESD.Read(f.Bytes).StateGroups)
                    foreach (var (sid, s) in states)
                        foreach (var c in s.EntryCommands.Concat(s.ExitCommands).Concat(s.WhileCommands).Concat(s.Conditions.SelectMany(All)))
                        {
                            counts[(c.CommandBank, c.CommandID)] = counts.GetValueOrDefault((c.CommandBank, c.CommandID)) + 1;
                            if (args.Length >= 2 && c.CommandBank == int.Parse(args[0]) && c.CommandID == int.Parse(args[1]) && shown++ < max)
                                Console.WriteLine($"{Path.GetFileName(path).Split('.')[0]} {Path.GetFileName(f.Name)} g{g} s{sid}: ({string.Join(", ", c.Arguments.Select(EzState.Disasm))})");
                        }
        if (args.Length < 2)
            foreach (var kv in counts.Where(kv => kv.Key.Item1 != 6).OrderBy(kv => kv.Key)) Console.WriteLine($"c{kv.Key.Item1}_{kv.Key.Item2}: {kv.Value}");
        else Console.WriteLine($"{counts.GetValueOrDefault((int.Parse(args[0]), int.Parse(args[1])))} uses");
    }

    static IEnumerable<ESD.CommandCall> All(ESD.Condition c) => c.PassCommands.Concat(c.Subconditions.SelectMany(All));
}

static class PartPos
{
    /// `partpos <map> <name part>`: where the map's parts whose name contains the text stand.
    public static void Run(string[] args)
    {
        var msb = MSBE.Read(Program.In("map", "mapstudio", args[0] + ".msb.dcx"));
        foreach (var p in msb.Parts.GetEntries().Where(p => p.Name.Contains(args[1])))
            Console.WriteLine($"{p.Name} entity {p.EntityID} at {p.Position.X:0.00} {p.Position.Y:0.00} {p.Position.Z:0.00} facing {p.Rotation.Y:0.#}");
    }
}

static class ParamRow
{
    /// `paramrow <param> <id> [id...] [--mod <folder>]`: the fields of rows, side by side where they differ from the first.
    public static void Run(string[] args)
    {
        int mi = Array.IndexOf(args, "--mod");
        string reg = mi >= 0 ? Path.Combine(args[mi + 1], "regulation.bin") : Program.In("regulation.bin");
        if (mi >= 0) args = args.Where((a, i) => i != mi && i != mi + 1).ToArray();
        var p = GameData.Load(reg)[args[0]];
        var rows = args.Skip(1).Select(a => p[int.Parse(a)]).ToList();
        foreach (var cell in rows[0].Cells)
        {
            var values = rows.Select(r => r == null ? "(no row)" : Convert.ToString(r[cell.Def.InternalName].Value)).ToList();
            if (rows.Count == 1 || values.Distinct().Count() > 1) Console.WriteLine($"{cell.Def.InternalName,-28} {string.Join("  |  ", values)}");
        }
    }
}

static class MapPeople
{
    /// `people <map>`: the map's characters with their names, talk scripts, entities and places.
    public static void Run(string[] args)
    {
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        var npc = gd["NpcParam"];
        foreach (var e in MSBE.Read(Program.In("map", "mapstudio", args[0] + ".msb.dcx")).Parts.Enemies.OrderBy(e => e.Name))
        {
            string name = npc[e.NPCParamID] is { } row ? text.Get("NpcName", row.Get<int>("nameId")) : null;
            Console.WriteLine($"{e.Name,-12} {name ?? "-",-28} npc {e.NPCParamID,-10} talk {e.TalkID,-10} entity {e.EntityID,-9} at {e.Position.X:0.0} {e.Position.Y:0.0} {e.Position.Z:0.0} facing {e.Rotation.Y:0} on {e.CollisionPartName}");
        }
    }
}

static class ModelParts
{
    /// `modelparts <model>`: every placement of a character model in the game's maps, with the
    /// settings that decide how it behaves.
    public static void Run(string[] args)
    {
        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            string map = Path.GetFileName(path).Split('.')[0];
            if (map.EndsWith("_99")) continue;
            foreach (var e in MSBE.Read(path).Parts.Enemies.Where(e => e.ModelName == args[0]))
                Console.WriteLine($"{map} {e.Name} npc {e.NPCParamID} think {e.ThinkParamID} talk {e.TalkID} init {e.CharaInitID} anim {e.BackupEventAnimID} spEffectSet {string.Join(",", e.SpEffectSetParamID)} entity {e.EntityID}");
        }
    }
}
