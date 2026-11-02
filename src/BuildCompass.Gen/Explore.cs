using SoulsFormats;

namespace BuildCompass.Gen;

/// Throwaway inspection of vanilla data: `explore <item name>` traces where an item's lots live.
static class Explore
{
    /// Reads the generated mod back to confirm every file parses and holds our additions.
    public static void Verify(string outDir = "mod")
    {
        string mod = Path.Combine(Program.Root, outDir);
        var gd = GameData.Load(Path.Combine(mod, "regulation.bin"));
        var wmp = gd["WorldMapPointParam"];
        Console.WriteLine($"regulation: version {gd.Version}, WMP rows {wmp.Rows.Count} (ours {wmp.Rows.Count(r => r.ID >= 95_000_000)}), sorted {wmp.Rows.Select(r => r.ID).SequenceEqual(wmp.Rows.Select(r => r.ID).OrderBy(i => i))}");
        var c = gd["EquipParamGoods"][2_909_000];
        Console.WriteLine($"compass goods: icon {c["iconId"].Value} type {c["goodsType"].Value} consume {c["isConsume"].Value} drop {c["isDrop"].Value} deposit {c["isDeposit"].Value} refId {c["refId_default"].Value}");
        var shopRow = gd["ShopLineupParam"][100508];
        Console.WriteLine($"Kalé shop row 100508: goods {shopRow?["equipId"].Value} type {shopRow?["equipType"].Value} price {shopRow?["value"].Value} qty {shopRow?["sellQuantity"].Value}");
        var sample = wmp[95_000_000];
        Console.WriteLine("first point: " + string.Join(" ", sample.Cells.Where(x => x.Def.InternalName is "eventFlagId" or "iconId" or "areaNo" or "gridXNo" or "gridZNo" or "posX" or "posZ" or "textId1" or "textDisableFlagId1" or "textId2" or "textEnableFlagId2").Select(x => $"{x.Def.InternalName}={x.Value}")));
        var item = BND4.Read(Path.Combine(mod, "msg", "engus", "item_dlc02.msgbnd.dcx"));
        var place = FMG.Read(item.Files.First(f => f.Name.EndsWith("\\PlaceName.fmg")).Bytes);
        Console.WriteLine($"PlaceName {sample.Get<int>("textId1")}: {place[sample.Get<int>("textId1")]}");
        var goodsName = FMG.Read(item.Files.First(f => f.Name.EndsWith("\\GoodsName.fmg")).Bytes);
        Console.WriteLine($"GoodsName: {goodsName[2_909_000]}");
        var menu = BND4.Read(Path.Combine(mod, "msg", "engus", "menu_dlc02.msgbnd.dcx"));
        var talk = FMG.Read(menu.Files.First(f => f.Name.EndsWith("\\EventTextForTalk.fmg")).Bytes);
        Console.WriteLine($"talk text: '{talk[GraceMenu.EntryText]}', '{talk[GraceMenu.TalkTextBase + 100]}', '{talk[GraceMenu.TalkTextBase + 101]}', '{talk[GraceMenu.DoneText]}'");
        Console.WriteLine($"stale common.emevd present: {File.Exists(Path.Combine(mod, "event", "common.emevd.dcx"))}");
        var tbnd = BND4.Read(Path.Combine(mod, "script", "talk", "m00_00_00_00.talkesdbnd.dcx"));
        var esd = ESD.Read(tbnd.Files.First(f => f.Name.EndsWith("t000001000.esd")).Bytes);
        Console.WriteLine($"talk script: {esd.StateGroups.Count} groups, ours present: {esd.StateGroups.Values.Any(g => g.Values.Any(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 149 && c.Arguments.Any(a => a.SequenceEqual(EzState.Lit(GraceMenu.TalkTextBase + 100))))))}");
    }

    /// Prints vanilla events that use DisplayGenericDialogAndSetEventFlags, with the instructions around it.
    public static void Dialogs()
    {
        int shown = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(Program.GameDir, "event"), "*.emevd.dcx"))
            foreach (var ev in EMEVD.Read(path).Events)
            {
                int idx = ev.Instructions.FindIndex(i => i.Bank == 2007 && i.ID == 10);
                if (idx < 0) continue;
                var a = ev.Instructions[idx].ArgData;
                Console.WriteLine($"FLAGS {Path.GetFileName(path)} ev {ev.ID} params={ev.Parameters.Count} entity={BitConverter.ToUInt32(a, 8)} dist={BitConverter.ToSingle(a, 12)} left={BitConverter.ToUInt32(a, 16)} right={BitConverter.ToUInt32(a, 20)} cancel={BitConverter.ToUInt32(a, 24)}");
                if (shown++ >= 0) continue;
                Console.WriteLine($"{Path.GetFileName(path)} event {ev.ID} rest={ev.RestBehavior}");
                for (int k = Math.Max(0, idx - 4); k < Math.Min(ev.Instructions.Count, idx + 12); k++)
                    Console.WriteLine($"  {(k == idx ? ">" : " ")}{ev.Instructions[k].Bank}[{ev.Instructions[k].ID}] {BitConverter.ToString(ev.Instructions[k].ArgData)}");
            }
    }

    /// Finds any param field named like *icon* holding one of the given values.
    public static void IconUse(string[] args)
    {
        int bi = Array.IndexOf(args, "--base");
        if (bi >= 0) { Program.Base = args[bi + 1]; args = args.Where((a, i) => i != bi && i != bi + 1).ToArray(); }
        var want = args.Select(int.Parse).ToHashSet();
        var gd = GameData.Load(Program.In("regulation.bin"));
        foreach (var (name, p) in gd.Params)
            foreach (var f in p.AppliedParamdef.Fields.Where(f => f.InternalName.Contains("icon", StringComparison.OrdinalIgnoreCase)))
            {
                var hits = p.Rows.Where(r => r[f.InternalName].Value is IConvertible c && want.Contains(Convert.ToInt32(c))).ToList();
                if (hits.Count > 0) Console.WriteLine($"{name}.{f.InternalName}: {string.Join(",", hits.Select(h => $"{h.ID}={h[f.InternalName].Value}").Take(6))}{(hits.Count > 6 ? $" (+{hits.Count - 6})" : "")}");
            }
    }

    /// Lists every MENU_MAP_N sprite in the map atlases with whether a map param places it, to find
    /// slots free for new marker art: `freeicons`.
    public static void FreeIcons()
    {
        var gd = GameData.Load(Program.In("regulation.bin"));
        var used = new Dictionary<int, string>();
        foreach (var (name, p) in gd.Params.Where(kv => kv.Key.StartsWith("WorldMap") || kv.Key.StartsWith("Bonfire")))
            foreach (var f in p.AppliedParamdef.Fields.Where(f => f.InternalName.Contains("icon", StringComparison.OrdinalIgnoreCase)))
                foreach (var r in p.Rows)
                    if (r[f.InternalName].Value is IConvertible c) used.TryAdd(Convert.ToInt32(c), $"{name}.{f.InternalName}");
        var lyt = BND4.Read(Program.In("menu", "hi", "01_common.sblytbnd.dcx"));
        foreach (var file in lyt.Files.Where(f => f.Name.Contains("MapCursor")))
        {
            var doc = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(file.Bytes));
            foreach (var e in doc.Root.Elements("SubTexture"))
            {
                var m = System.Text.RegularExpressions.Regex.Match((string)e.Attribute("name"), @"^MENU_MAP_(\d+)\.png$");
                if (!m.Success) continue;
                int id = int.Parse(m.Groups[1].Value);
                string region = $"{e.Attribute("x")},{e.Attribute("y")} {e.Attribute("width")}x{e.Attribute("height")}";
                Console.WriteLine($"{id,5} {Path.GetFileNameWithoutExtension(file.Name),-22} {region,-20} {(used.TryGetValue(id, out var u) ? "used by " + u : "FREE")}");
            }
        }
    }

    /// Where goods whose name contains the text come from: `whereis <text>` (map treasures, enemy
    /// drops, event rewards, shops).
    public static void WhereIs(string[] args)
    {
        string want = string.Join(" ", args);
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        var goods = gd["EquipParamGoods"].Rows.Where(r => text.Get("GoodsName", r.ID)?.Contains(want, StringComparison.OrdinalIgnoreCase) == true)
            .ToDictionary(r => r.ID, r => text.Get("GoodsName", r.ID));
        var lots = gd["ItemLotParam_map"].Rows.Where(l => Enumerable.Range(1, 8).Any(s => l.Get<int>($"lotItemCategory0{s}") == 1 && goods.ContainsKey(l.Get<int>($"lotItemId0{s}"))))
            .ToDictionary(l => l.ID, l => goods[Enumerable.Range(1, 8).Select(s => l.Get<int>($"lotItemId0{s}")).First(goods.ContainsKey)]);
        // A lot is often the second or later in a chain started by a treasure/drop/reward lot.
        int? Owner(int lot, Func<int, bool> starts) => Enumerable.Range(0, 10).Select(k => lot - k).Cast<int?>().FirstOrDefault(l => starts(l.Value));
        var npcLots = gd["NpcParam"].Rows.Where(n => n.Get<int>("itemLotId_map") > 0).GroupBy(n => n.Get<int>("itemLotId_map")).ToDictionary(g => g.Key, g => g.Select(n => n.ID).ToList());
        var treasures = new Dictionary<int, string>();
        var dropMaps = new Dictionary<int, List<string>>();
        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            string map = Path.GetFileName(path).Split('.')[0];
            var msb = MSBE.Read(path);
            foreach (var t in msb.Events.Treasures) if (t.ItemLotID > 0) treasures.TryAdd(t.ItemLotID, $"{map} treasure {t.TreasurePartName}");
            foreach (var e in msb.Parts.Enemies)
                foreach (var (lot, _) in npcLots.Where(kv => kv.Value.Contains(e.NPCParamID)))
                    (dropMaps.TryGetValue(lot, out var l) ? l : dropMaps[lot] = new()).Add($"{map} {e.Name}");
        }
        var rewards = new Rewards(Rewards.LoadEnemies()).All.GroupBy(r => r.LotId).ToDictionary(g => g.Key, g => g.First());
        foreach (var (lot, name) in lots.OrderBy(kv => kv.Value))
        {
            var where = new List<string>();
            if (Owner(lot, treasures.ContainsKey) is int t) where.Add(treasures[t]);
            if (Owner(lot, dropMaps.ContainsKey) is int d) where.Add("drop " + string.Join(", ", dropMaps[d].Take(3)));
            if (Owner(lot, rewards.ContainsKey) is int rw) where.Add($"reward {rewards[rw].Enemy.Map} {rewards[rw].Enemy.Part.Name}{(rewards[rw].Boss ? " (boss)" : "")}");
            Console.WriteLine($"{name,-44} lot {lot,-11} {(where.Count > 0 ? string.Join(" | ", where) : "?")}");
        }
        foreach (var s in gd["ShopLineupParam"].Rows.Where(s => s.Get<int>("equipType") == 3 && goods.ContainsKey(s.Get<int>("equipId"))))
            Console.WriteLine($"{goods[s.Get<int>("equipId")],-44} shop row {s.ID}");
        // Enemy loot table (NpcParam.itemLotId_enemy).
        var enemyLots = gd["ItemLotParam_enemy"].Rows.Where(l => Enumerable.Range(1, 8).Any(s => l.Get<int>($"lotItemCategory0{s}") == 1 && goods.ContainsKey(l.Get<int>($"lotItemId0{s}")))).ToList();
        foreach (var l in enemyLots)
        {
            var npcs = gd["NpcParam"].Rows.Where(n => Enumerable.Range(0, 10).Any(k => n.Get<int>("itemLotId_enemy") == l.ID - k)).Select(n => n.ID).ToList();
            Console.WriteLine($"{goods[Enumerable.Range(1, 8).Select(s => l.Get<int>($"lotItemId0{s}")).First(goods.ContainsKey)],-44} enemy lot {l.ID} flag {l.Get<uint>("getItemFlagId")} npcs {string.Join(",", npcs.Take(5))}");
        }
    }

    /// Prints every instruction (any event) whose 4-byte-aligned args contain a value: `mentions <value>`.
    public static void Mentions(string[] args)
    {
        int v = int.Parse(args[0]);
        foreach (var path in Program.InFiles("*.emevd.dcx", "event"))
            foreach (var ev in EMEVD.Read(path).Events)
                foreach (var ins in ev.Instructions)
                    if (Enumerable.Range(0, ins.ArgData.Length / 4).Any(k => BitConverter.ToInt32(ins.ArgData, k * 4) == v))
                        Console.WriteLine($"{Path.GetFileName(path)} ev {ev.ID}: {ins.Bank}:{ins.ID} {BitConverter.ToString(ins.ArgData)}");
    }

    /// Dumps one event's instructions and parameter mappings, and its first call sites: `event <id>`.
    public static void EventDump(string[] args)
    {
        long want = long.Parse(args[0]);
        int shownCalls = 0;
        foreach (var path in Program.InFiles("*.emevd.dcx", "event"))
            foreach (var ev in EMEVD.Read(path).Events)
            {
                if (ev.ID == want)
                {
                    Console.WriteLine($"== {Path.GetFileName(path)} ev {ev.ID}");
                    for (int i = 0; i < ev.Instructions.Count; i++)
                        Console.WriteLine($"  [{i}] {ev.Instructions[i].Bank}:{ev.Instructions[i].ID} {BitConverter.ToString(ev.Instructions[i].ArgData)}");
                    foreach (var p in ev.Parameters)
                        Console.WriteLine($"  param: ins {p.InstructionIndex} target {p.TargetStartByte} <- src {p.SourceStartByte} ({p.ByteCount} bytes)");
                }
                foreach (var ins in ev.Instructions)
                    if (ins.Bank == 2000 && (ins.ID == 0 || ins.ID == 6) && ins.ArgData.Length >= 8 && BitConverter.ToUInt32(ins.ArgData, 4) == want && shownCalls++ < 5)
                        Console.WriteLine($"  call from {Path.GetFileName(path)} ev {ev.ID}: {BitConverter.ToString(ins.ArgData[8..])}");
            }
    }

    /// Lists item lots that event scripts award when an enemy dies: `rewards`.
    public static void RewardList()
    {
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        var lots = gd["ItemLotParam_map"];
        var npc = gd["NpcParam"];
        var enemies = Rewards.LoadEnemies();
        Console.WriteLine($"enemies with entity ids: {enemies.Count}");
        var rewards = new Rewards(enemies);
        string ItemName(int cat, int id) => cat switch
        {
            1 => text.Get("GoodsName", id), 2 => text.Get("WeaponName", id - id % 100), 3 => text.Get("ProtectorName", id),
            4 => text.Get("AccessoryName", id), _ => null,
        };
        foreach (var r in rewards.All.OrderBy(r => r.Enemy.Map))
        {
            var items = new List<string>();
            for (int id = r.LotId; id < r.LotId + 10 && lots[id] != null; id++)
                for (int s = 1; s <= 8; s++)
                {
                    var name = ItemName(lots[id].Get<int>($"lotItemCategory0{s}"), lots[id].Get<int>($"lotItemId0{s}"));
                    if (name != null) items.Add(name);
                }
            var n = npc[r.Enemy.NpcId];
            string who = (r.NameId > 0 ? text.Get("NpcName", r.NameId) : null) ?? (n != null ? text.Get("NpcName", n.Get<int>("nameId")) : null) ?? $"npc {r.Enemy.NpcId}";
            Console.WriteLine($"{(r.Boss ? "BOSS" : "    ")} lot {r.LotId,-10} {r.Enemy.Map} {r.Enemy.Part.Name,-14} {who,-34} {string.Join("; ", items.Distinct())}  [{r.Source}]");
        }
        Console.WriteLine($"total {rewards.All.Count}");
    }

    /// Prints the shop lineup rows around whichever row sells the named goods.
    public static void Shop(string[] args)
    {
        var gd = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"));
        var text = new Text(Path.Combine(Program.GameDir, "msg", "engus", "item_dlc02.msgbnd.dcx"));
        var shop = gd["ShopLineupParam"];
        Console.WriteLine("fields: " + string.Join(" ", shop.AppliedParamdef.Fields.Select(f => f.InternalName)));
        string name = string.Join(" ", args);
        foreach (var hit in shop.Rows.Where(r => r.Get<int>("equipType") == 3 && text.Get("GoodsName", r.Get<int>("equipId")) == name))
        {
            Console.WriteLine($"-- {name} at row {hit.ID}");
            foreach (var r in shop.Rows.Where(r => r.ID / 100 == hit.ID / 100))
                Console.WriteLine($"  {r.ID} type={r["equipType"].Value} id={r["equipId"].Value} '{text.Get(r.Get<int>("equipType") == 3 ? "GoodsName" : "WeaponName", r.Get<int>("equipId"))}' value={r["value"].Value} qty={r["sellQuantity"].Value} stockFlag={r["eventFlag_forStock"].Value} releaseFlag={r["eventFlag_forRelease"].Value}");
        }
    }

    /// Prints vanilla uses of one EMEVD instruction: `instr <bank> <id>`.
    public static void Instr(string[] args)
    {
        int bank = int.Parse(args[0]), id = int.Parse(args[1]), shown = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(Program.GameDir, "event"), "*.emevd.dcx"))
            foreach (var ev in EMEVD.Read(path).Events)
                foreach (var ins in ev.Instructions.Where(i => i.Bank == bank && i.ID == id))
                    if (shown++ < 12) Console.WriteLine($"{Path.GetFileName(path)} ev {ev.ID} params={ev.Parameters.Count}: {BitConverter.ToString(ins.ArgData)}");
        Console.WriteLine($"total {shown}");
        if (args.Length > 2)
        {
            var ev = EMEVD.Read(Path.Combine(Program.GameDir, "event", "common.emevd.dcx")).Events.First(e => e.ID == long.Parse(args[2]));
            foreach (var ins in ev.Instructions.Take(40)) Console.WriteLine($"  {ins.Bank}[{ins.ID}] {BitConverter.ToString(ins.ArgData)}");
        }
    }

    /// goodsType of goods whose id is also a Magic row, with sample names.
    public static void SpellTypes()
    {
        var gd = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"));
        var text = new Text(Path.Combine(Program.GameDir, "msg", "engus", "item_dlc02.msgbnd.dcx"));
        var goods = gd["EquipParamGoods"];
        foreach (var grp in gd["Magic"].Rows.Select(m => goods[m.ID]).Where(g => g != null).GroupBy(g => g.Get<int>("goodsType")))
            Console.WriteLine($"goodsType {grp.Key}: {grp.Count()} e.g. {string.Join(", ", grp.Take(5).Select(g => $"{g.ID} {text.Get("GoodsName", g.ID)}"))}");
    }

    public static void Run(string[] args)
    {
        string g = Program.GameDir;
        var gd = GameData.Load(Path.Combine(g, "regulation.bin"));
        var text = new Text(Path.Combine(g, "msg", "engus", "item_dlc02.msgbnd.dcx"));
        string name = string.Join(" ", args);
        var ids = gd["EquipParamWeapon"].Rows.Where(r => text.Get("WeaponName", r.ID) == name).Select(r => r.ID).ToHashSet();
        Console.WriteLine($"{name}: weapon ids {string.Join(",", ids)}");
        var lots = gd["ItemLotParam_map"].Rows.Where(r => Enumerable.Range(1, 8).Any(s => ids.Contains(r.Get<int>($"lotItemId0{s}") - r.Get<int>($"lotItemId0{s}") % 100) && r.Get<int>($"lotItemCategory0{s}") == 2)).ToList();
        foreach (var l in lots)
            Console.WriteLine($"  lot {l.ID} flag {l["getItemFlagId"].Value} item {l["lotItemId01"].Value} cat {l["lotItemCategory01"].Value} pts {l["lotItemBasePoint01"].Value}");
        var lotIds = lots.Select(l => l.ID).ToHashSet();
        foreach (var path in Directory.GetFiles(Path.Combine(g, "map", "mapstudio"), "*.msb.dcx"))
        {
            var msb = MSBE.Read(path);
            foreach (var t in msb.Events.Treasures.Where(t => lotIds.Contains(t.ItemLotID) || lotIds.Contains(t.ItemLotID + 1) || lotIds.Any(l => l > t.ItemLotID && l < t.ItemLotID + 10)))
                Console.WriteLine($"  {Path.GetFileName(path)} treasure {t.Name} lot {t.ItemLotID} part {t.TreasurePartName}");
        }    }
}

/// How vanilla places NPC marks (icon 80) relative to the nearest landmark.
static class NpcOffsets
{
    public static void Run()
    {
        var gd = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"));
        Builder.legacyConv = gd["WorldMapLegacyConvParam"].Rows;
        var wmp = gd["WorldMapPointParam"].Rows;
        var pos = wmp.ToDictionary(r => r.ID, r => Builder.MapPos(r.Get<byte>("areaNo"), r.Get<byte>("gridXNo"), r.Get<byte>("gridZNo"),
            new System.Numerics.Vector3(r.Get<float>("posX"), 0, r.Get<float>("posZ"))));
        var npcs = wmp.Where(r => r.Get<int>("iconId") == 80).ToList();
        var marks = wmp.Where(r => r.Get<int>("iconId") is not (80 or 83 or 84 or 85 or 87 or 88)).ToList();
        foreach (var n in npcs.Take(40))
        {
            var p = pos[n.ID];
            var near = marks.Where(m => pos[m.ID].space == p.space).OrderBy(m => System.Numerics.Vector2.Distance(pos[m.ID].xz, p.xz)).FirstOrDefault();
            if (near == null) { Console.WriteLine($"npc {n.ID} area {n["areaNo"].Value}: no landmark in space"); continue; }
            var d = p.xz - pos[near.ID].xz;
            Console.WriteLine($"npc {n.ID} a{n["areaNo"].Value} -> landmark {near.ID} icon {near["iconId"].Value}: dx {d.X:0.0} dz {d.Y:0.0} dist {d.Length():0.0}");
        }
    }
}
