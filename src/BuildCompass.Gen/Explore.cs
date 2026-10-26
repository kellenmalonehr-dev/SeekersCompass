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
