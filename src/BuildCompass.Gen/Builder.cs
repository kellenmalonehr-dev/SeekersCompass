using System.Numerics;
using SoulsFormats;

namespace BuildCompass.Gen;

/// Generates the Mod Engine 2 mod folder from vanilla data.
static class Builder
{
    const int WmpRowBase = 95_000_000;
    const int PlaceTextBase = 950_000_000;
    public const int CompassGoodsId = 2_909_000;
    const string CompassName = "Seeker's Compass";

    // Settings live in unreferenced slots of vanilla's heavily used 78xxx block, which persists
    // across reloads. Order matters: saves keep their choices only if flags keep their slots
    // (slots 5-11 belonged to the old yes/no prompt flow and stay reserved).
    static readonly uint[] SettingFlagCandidates = Enumerable.Range(78990, 10).Concat(Enumerable.Range(78980, 10)).Select(i => (uint)i).ToArray();
    static readonly uint[] DerivedFlagCandidates = Enumerable.Range(78900, 80).Select(i => (uint)i).ToArray();

    // Kalé (Church of Elleh) sells rows 100500-100525; 100508 is free. Stock flags follow the row
    // number in vanilla (1005NN -> 1500N0).
    const int CompassShopRow = 100508;
    const uint CompassShopFlag = 150080;
    const int CompassPrice = 100;

    public static readonly Dictionary<Stat, string> StatNames = new()
    {
        [Stat.STR] = "Strength", [Stat.DEX] = "Dexterity", [Stat.INT] = "Intelligence", [Stat.FAI] = "Faith", [Stat.ARC] = "Arcane",
    };
    public static readonly Dictionary<Kind, string> KindNames = new()
    {
        [Kind.Weapon] = "Weapons", [Kind.Spell] = "Spells", [Kind.Armor] = "Armor", [Kind.Talisman] = "Talismans",
    };

    public static void Run(string[] args)
    {
        bool iconTest = args.Contains("--icon-test");
        // --base <folder>: build on top of another mod (e.g. Convergence). --out <folder>: output folder.
        int bi = Array.IndexOf(args, "--base"), oi = Array.IndexOf(args, "--out");
        if (bi >= 0) Program.Base = args[bi + 1];
        string root = Program.Root, mod = oi >= 0 ? Path.GetFullPath(Path.Combine(root, args[oi + 1])) : Path.Combine(root, "mod");
        Console.WriteLine($"base: {Program.Base ?? "vanilla"} -> {mod}");
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        var tags = new Tags(Path.Combine(root, "data", "tags.json"));

        // --- flags
        var used = FlagScan.UsedFlags(gd);
        var settings = SettingFlagCandidates.Where(f => !used.Contains(f)).ToArray();
        if (settings.Length < 16) throw new Exception("not enough free flags in candidate range");
        if (used.Contains(CompassShopFlag)) throw new Exception("shop stock flag in use");
        var statFlag = Enum.GetValues<Stat>().ToDictionary(s => s, s => settings[(int)s]);
        var kindFlag = Enum.GetValues<Kind>().ToDictionary(k => k, k => settings[12 + (int)k]); // "show all" toggles
        // Derived visibility flags, recomputed by the grace menu after every change (see GraceMenu).
        var derived = DerivedFlagCandidates.Where(f => !used.Contains(f)).ToArray();
        if (derived.Length < 26) throw new Exception("not enough free derived flags");
        var vis = new Visibility(derived);
        Console.WriteLine($"flags: stats {string.Join(",", statFlag.Values)} show-all {string.Join(",", kindFlag.Values)}");

        // --- catalog
        var cat = new Catalog(gd, text, tags);
        cat.Build();
        Console.WriteLine($"merged-tile name/position mismatches: {cat.TileMismatches}");
        Console.WriteLine($"placements: {cat.Placements.Count} (" + string.Join(", ", cat.Placements.GroupBy(p => p.Kind).Select(x => $"{x.Key} {x.Count()}")) + ")");
        foreach (var s in Enum.GetValues<Stat>())
            Console.WriteLine($"  {s}: {cat.Placements.Count(p => p.Stats.Contains(s))}");
        WriteReport(Path.Combine(root, "out", "placements.csv"), cat.Placements);
        File.WriteAllLines(Path.Combine(root, "out", "untagged_armor.csv"), cat.UntaggedArmor.OrderBy(u => u.name).Select(u => $"\"{u.name}\",{u.map},{u.weight}"));

        // --- params
        var wmp = gd["WorldMapPointParam"];
        var template = wmp[87210100] ?? throw new Exception("WMP template row missing");
        // Hover text ids are shared by every language; each language's PlaceName gets its own wording.
        var hoverIds = new List<(Placement p, int nameText, int collectedText)>();
        var fixedPlaceText = new SortedDictionary<int, string>(); // language-neutral entries (icon test)
        int rowId = WmpRowBase, textId = PlaceTextBase;
        legacyConv = gd["WorldMapLegacyConvParam"].Rows;
        var snap = new LocationSnap(wmp.Rows, gd["BonfireWarpParam"].Rows);

        foreach (var p in cat.Placements)
        {
            int nameText = textId++, collectedText = textId++;
            hoverIds.Add((p, nameText, collectedText));
            var basePos = snap.Place(p);
            // A map point shows while one flag is on, so an item gets one row per matching stat plus
            // one for its category; the derived flags encode the menu's AND/OR rules (see Visibility).
            var showFlags = p.Stats.Select(s => vis.StatInKind[(p.Kind, s)]).Append(vis.AllOfKind[p.Kind]);
            int dup = 0;
            foreach (var flag in showFlags)
            {
                var pos = basePos + new Vector3(0.4f * dup++, 0, 0); // nudge stacked rows apart
                AddPoint(wmp, template, rowId++, Icons.Slot[p.Kind], flag, p.Area, p.GridX, p.GridZ, pos,
                    (nameText, 0, p.PickupFlag), (collectedText, p.PickupFlag, 0));
            }
        }
        Console.WriteLine($"snapped onto locations: {snap.Snapped} items at {snap.Locations} locations");

        if (iconTest) AddIconPalette(gd, text, wmp, template, statFlag[Stat.STR], fixedPlaceText, ref rowId, ref textId);
        wmp.Rows = wmp.Rows.OrderBy(r => r.ID).ToList();
        Console.WriteLine($"map points: {rowId - WmpRowBase}");

        AddCompassItem(gd, text);
        AddToKaleShop(gd);

        var reg = gd.Regulation;
        foreach (var name in new[] { "WorldMapPointParam", "EquipParamGoods", "ShopLineupParam" })
            reg.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == name).Bytes = gd[name].Write();
        Directory.CreateDirectory(mod);
        SoulsFormats.Cryptography.RegulationDecryptor.EncryptERRegulation(Path.Combine(mod, "regulation.bin"), reg);

        // --- map icons
        Icons.Build(mod);

        // --- text, for every unpacked game language
        var langs = Loc.All();
        Console.WriteLine($"languages: {string.Join(" ", langs.Select(l => l.Lang))}");
        foreach (var loc in langs)
        {
            var placeText = new Dictionary<int, string>(fixedPlaceText);
            foreach (var (p, nameText, collectedText) in hoverIds)
            {
                // The item's own name in this language (Convergence's new items may only exist in English).
                string name = loc.Name(p.Kind, p.ItemId) ?? p.Name;
                var details = p.Render(loc);
                placeText[nameText] = loc.Fit(name, details);
                placeText[collectedText] = loc.Fit(name, details, collected: true);
            }
            var goodsText = new Dictionary<string, (int, string)>
            {
                ["GoodsName"] = (CompassGoodsId, CompassName),
                ["GoodsInfo"] = (CompassGoodsId, loc.CompassDescription),
                ["GoodsCaption"] = (CompassGoodsId, loc.CompassDescription),
            };
            var talkText = GraceMenu.Texts(loc);
            Directory.CreateDirectory(Path.Combine(mod, "msg", loc.Lang));
            foreach (var bnd in new[] { "item.msgbnd.dcx", "item_dlc01.msgbnd.dcx", "item_dlc02.msgbnd.dcx" })
                PatchFmgs(Program.In("msg", loc.Lang, bnd), Path.Combine(mod, "msg", loc.Lang, bnd), fmgName =>
                    fmgName == "PlaceName" ? placeText
                    : goodsText.TryGetValue(fmgName, out var e) ? new Dictionary<int, string> { [e.Item1] = e.Item2 } : null);
            foreach (var bnd in new[] { "menu.msgbnd.dcx", "menu_dlc01.msgbnd.dcx", "menu_dlc02.msgbnd.dcx" })
                PatchFmgs(Program.In("msg", loc.Lang, bnd), Path.Combine(mod, "msg", loc.Lang, bnd), fmgName =>
                    fmgName == "EventTextForTalk" ? talkText : null);
        }

        // --- grace menu (talk script)
        GraceMenu.Build(mod, statFlag, kindFlag, vis);

        // The yes/no prompt event is gone; drop the old common.emevd so it can't keep running.
        string staleEvent = Path.Combine(mod, "event", "common.emevd.dcx");
        if (File.Exists(staleEvent)) File.Delete(staleEvent);

        Console.WriteLine($"skipped: {cat.Skipped.Count}");
        Console.WriteLine($"wrote {mod}");
    }

    internal static List<PARAM.Row> legacyConv;

    /// A comparable map position. Open-world tiles share one space (tile * 256 + local). Other maps
    /// (legacy dungeons, caves, catacombs) are drawn on the world map through WorldMapLegacyConvParam,
    /// so they're converted the same way: nearest anchor in their map, then that anchor's offset.
    internal static (int space, Vector2 xz) MapPos(byte area, byte gx, byte gz, Vector3 local, int depth = 0)
    {
        if (area is 60 or 61) return (area, new Vector2(gx * 256 + local.X, gz * 256 + local.Z));
        var anchor = depth < 4 ? legacyConv?
            .Where(r => r.Get<byte>("srcAreaNo") == area && r.Get<byte>("srcGridXNo") == gx && r.Get<byte>("srcGridZNo") == gz)
            .OrderBy(r => Vector2.Distance(new Vector2(r.Get<float>("srcPosX"), r.Get<float>("srcPosZ")), new Vector2(local.X, local.Z)))
            .FirstOrDefault() : null;
        if (anchor == null) return (area * 1_000_000 + gx * 1000 + gz, new Vector2(local.X, local.Z));
        var dst = new Vector3(anchor.Get<float>("dstPosX"), 0, anchor.Get<float>("dstPosZ"))
            + new Vector3(local.X - anchor.Get<float>("srcPosX"), 0, local.Z - anchor.Get<float>("srcPosZ"));
        return MapPos(anchor.Get<byte>("dstAreaNo"), anchor.Get<byte>("dstGridXNo"), anchor.Get<byte>("dstGridZNo"), dst, depth + 1);
    }


    static void AddPoint(PARAM wmp, PARAM.Row template, int id, ushort icon, uint showFlag, byte area, byte gx, byte gz, Vector3 pos,
        params (int text, uint enable, uint disable)[] texts)
    {
        var r = new PARAM.Row(template) { ID = id, Name = "" };
        r.Set("disableParam_NT", 0);
        r.Set("eventFlagId", showFlag);
        r.Set("distViewEventFlagId", 0);
        r.Set("iconId", icon);
        r.Set("isAreaIcon", 0);
        r.Set("isEnableNoText", 0);
        r.Set("clearedEventFlagId", 0);
        // The underground (m12) has its own map layer.
        r.Set("dispMask00", area == 12 ? 0 : 1);
        r.Set("dispMask01", area == 12 ? 1 : 0);
        r.Set("angle", 0f);
        r.Set("areaNo", area); r.Set("gridXNo", gx); r.Set("gridZNo", gz);
        r.Set("posX", pos.X); r.Set("posY", pos.Y); r.Set("posZ", pos.Z);
        for (int i = 1; i <= 8; i++)
        {
            var t = i <= texts.Length ? texts[i - 1] : (-1, 0u, 0u);
            r.Set($"textId{i}", t.Item1);
            r.Set($"textEnableFlagId{i}", t.Item2);
            r.Set($"textDisableFlagId{i}", t.Item3);
            r.Set($"textType{i}", 0); // 0 = PlaceName
        }
        wmp.Rows.Add(r);
    }

    /// Debug: one labeled mark per vanilla icon id near The First Step, to pick icons in-game.
    static void AddIconPalette(GameData gd, Text text, PARAM wmp, PARAM.Row template, uint showFlag,
        SortedDictionary<int, string> placeText, ref int rowId, ref int textId)
    {
        var first = gd["BonfireWarpParam"].Rows.First(r => text.Get("PlaceName", r.Get<int>("textId1")) == "The First Step");
        var icons = gd["WorldMapPointParam"].Rows.Select(r => r.Get<int>("iconId"))
            .Concat(gd["BonfireWarpParam"].Rows.Select(r => r.Get<int>("iconId")))
            .Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
        int gx0 = first.Get<byte>("gridXNo"), gz0 = first.Get<byte>("gridZNo");
        var origin = new Vector3(first.Get<float>("posX"), first.Get<float>("posY"), first.Get<float>("posZ"));
        const float spacing = 80; // wide enough that the large landmark sprites don't overlap
        for (int i = 0; i < icons.Count; i++)
        {
            int t = textId++;
            placeText[t] = $"Icon {icons[i]}";
            // Row 0 is the southernmost row, column 0 the westernmost.
            var p = origin + new Vector3((i % 10) * spacing - 4.5f * spacing, 0, (i / 10) * spacing + 60);
            // Re-home the point into whichever 256 m tile it lands in.
            int gx = gx0 + (int)MathF.Floor((p.X + 128) / 256), gz = gz0 + (int)MathF.Floor((p.Z + 128) / 256);
            var local = new Vector3(p.X - (gx - gx0) * 256, p.Y, p.Z - (gz - gz0) * 256);
            AddPoint(wmp, template, rowId++, (ushort)icons[i], showFlag, first.Get<byte>("areaNo"), (byte)gx, (byte)gz, local, (t, 0, 0));
        }
        Console.WriteLine($"icon palette: {icons.Count} icons near The First Step: {string.Join(",", icons)}");
    }

    /// The compass is a key item: carrying it unlocks the grace menu entry. It is cloned from the
    /// Rusty Key, a key item that can't be used, dropped or stored, so it never leaves the player.
    static void AddCompassItem(GameData gd, Text text)
    {
        var goods = gd["EquipParamGoods"];
        if (goods[CompassGoodsId] != null) throw new Exception("goods id collision");
        int keyId = goods.Rows.First(r => text.Get("GoodsName", r.ID) == "Rusty Key").ID;

        var c = new PARAM.Row(goods[keyId]) { ID = CompassGoodsId, Name = CompassName };
        c.Set("iconId", Icons.CompassIconId); // our compass picture (see Icons.AddItemIcon)
        c.Set("maxNum", 1);
        goods.Rows.Add(c);
        goods.Rows = goods.Rows.OrderBy(r => r.ID).ToList();
    }

    /// Adds the compass to Kalé's lineup, cloned from his Telescope row. Unlimited stock; the goods'
    /// maxNum of 1 stops buying a second one.
    static void AddToKaleShop(GameData gd)
    {
        var shop = gd["ShopLineupParam"];
        if (shop[CompassShopRow] != null) throw new Exception("shop row collision");
        var row = new PARAM.Row(shop[100513]) { ID = CompassShopRow, Name = CompassName };
        row.Set("equipType", 3);
        row.Set("equipId", CompassGoodsId);
        row.Set("value", CompassPrice);
        row.Set("sellQuantity", -1);
        row.Set("eventFlag_forStock", CompassShopFlag);
        row.Set("eventFlag_forRelease", 0u);
        shop.Rows.Add(row);
        shop.Rows = shop.Rows.OrderBy(r => r.ID).ToList();
    }

    static void PatchFmgs(string src, string dst, Func<string, Dictionary<int, string>> entriesFor)
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

    static void WriteReport(string path, List<Placement> ps)
    {
        var englishLoc = Loc.For("engus");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var w = new StreamWriter(path);
        w.WriteLine("kind,name,stats,map,pos,source,hover");
        foreach (var p in ps.OrderBy(p => p.Kind).ThenBy(p => p.Name))
            w.WriteLine($"{p.Kind},\"{p.Name}\",{string.Join("/", p.Stats)},m{p.Area}_{p.GridX:00}_{p.GridZ:00},\"{p.Pos.X:0.0} {p.Pos.Y:0.0} {p.Pos.Z:0.0}\",{p.Source},\"{englishLoc.Fit(p.Name, p.Render(englishLoc)).Replace("\"", "'")}\"");
    }
}

/// Flags the map points actually watch, derived from the menu's toggles:
/// - StatInKind[k, s] = stat s is on AND (category k is on OR no category is on)
/// - AllOfKind[k]     = category k is on AND no stat is on
/// So stats alone mark matching items of every kind, categories alone mark everything in them,
/// and both together mark only matching items inside the chosen categories.
class Visibility
{
    public readonly uint AnyStat, AnyKind;
    public readonly Dictionary<(Kind, Stat), uint> StatInKind = new();
    public readonly Dictionary<Kind, uint> AllOfKind = new();

    public Visibility(uint[] free)
    {
        int i = 0;
        AnyStat = free[i++]; AnyKind = free[i++];
        foreach (var k in Enum.GetValues<Kind>())
            foreach (var s in Enum.GetValues<Stat>())
                StatInKind[(k, s)] = free[i++];
        foreach (var k in Enum.GetValues<Kind>()) AllOfKind[k] = free[i++];
    }
}
