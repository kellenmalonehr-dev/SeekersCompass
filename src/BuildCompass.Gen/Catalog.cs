using System.Numerics;
using System.Text.Json;
using SoulsFormats;

namespace BuildCompass.Gen;

enum Stat { STR, DEX, INT, FAI, ARC }

enum Kind { Weapon, Spell, Armor, Talisman }

/// One collectable item at one place in the world.
record Placement(Kind Kind, int ItemId, string Name, string[] Details, List<Stat> Stats,
    uint PickupFlag, byte Area, byte GridX, byte GridZ, Vector3 Pos, string Source);

/// Walks MSB treasures and unique enemy drops, resolves item lots, and tags each item with stats.
class Catalog
{
    readonly GameData gd;
    readonly Text text;
    readonly Tags tags;
    public readonly List<Placement> Placements = new();
    public readonly List<string> Skipped = new();
    public readonly List<(string name, string map, float weight)> UntaggedArmor = new();

    public Catalog(GameData gd, Text text, Tags tags) { this.gd = gd; this.text = text; this.tags = tags; }

    public void Build()
    {
        var lots = gd["ItemLotParam_map"];
        var lotById = lots.Rows.ToDictionary(r => r.ID);
        var npc = gd["NpcParam"];
        var npcById = npc.Rows.ToDictionary(r => r.ID);
        var seenLots = new HashSet<int>();

        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            string map = Path.GetFileName(path).Split('.')[0]; // mAA_BB_CC_DD
            var parts = map.Substring(1).Split('_').Select(int.Parse).ToArray();
            int area = parts[0];
            bool openWorld = area == 60 || area == 61;
            // _99 are unused variants. Open-world _01/_02 are 2x2 / 4x4 merged tiles that also hold
            // pickups; their positions are converted back to the small (_00) tile grid.
            if (parts[3] == 99 || (!openWorld && parts[3] != 0)) continue;

            var msb = MSBE.Read(path);
            var partByName = msb.Parts.GetEntries().ToDictionary(p => p.Name, p => p);

            foreach (var t in msb.Events.Treasures)
            {
                if (t.ItemLotID <= 0 || t.TreasurePartName == null || !partByName.TryGetValue(t.TreasurePartName, out var part)) continue;
                var loc = Locate(area, parts, part.Position, part.Name);
                foreach (var lotId in Chain(t.ItemLotID, lotById))
                    if (seenLots.Add(lotId))
                        AddLot(lotById[lotId], loc.area, loc.gx, loc.gz, loc.pos, $"{map} treasure");
            }

            foreach (var e in msb.Parts.Enemies)
            {
                if (!npcById.TryGetValue(e.NPCParamID, out var n)) continue;
                int lotId = n.Get<int>("itemLotId_map");
                if (lotId <= 0) continue;
                foreach (var id in Chain(lotId, lotById))
                {
                    var row = lotById[id];
                    // Only unique drops have a pickup flag; repeatable farm drops would flood the map.
                    if (row.Get<uint>("getItemFlagId") == 0 || !seenLots.Add(id)) continue;
                    var loc = Locate(area, parts, e.Position, e.Name);
                    AddLot(row, loc.area, loc.gx, loc.gz, loc.pos, $"{map} drop {e.Name}");
                }
            }
        }
    }

    public int TileMismatches;

    /// Converts a part position to small-tile coordinates. Tile-local origins are tile centers.
    (byte area, byte gx, byte gz, Vector3 pos) Locate(int area, int[] parts, Vector3 local, string partName)
    {
        if (parts[3] == 0) return ((byte)area, (byte)parts[1], (byte)parts[2], local);
        float size = 256 << parts[3];
        float wx = parts[1] * size + size / 2 + local.X, wz = parts[2] * size + size / 2 + local.Z;
        int gx = (int)MathF.Floor(wx / 256), gz = (int)MathF.Floor(wz / 256);
        // Merged-tile parts are usually named "m60_XX_YY_00-..." after their home tile; use it as a check.
        var m = System.Text.RegularExpressions.Regex.Match(partName ?? "", @"^m6\d_(\d\d)_(\d\d)_00-");
        if (m.Success && (int.Parse(m.Groups[1].Value) != gx || int.Parse(m.Groups[2].Value) != gz)) TileMismatches++;
        return ((byte)area, (byte)gx, (byte)gz, new Vector3(wx - (gx * 256 + 128), local.Y, wz - (gz * 256 + 128)));
    }

    /// A treasure's lot plus the consecutive lots chained after it.
    static IEnumerable<int> Chain(int first, Dictionary<int, PARAM.Row> lotById)
    {
        for (int id = first; id < first + 10 && lotById.ContainsKey(id); id++)
            yield return id;
    }

    void AddLot(PARAM.Row lot, byte area, byte gx, byte gz, Vector3 pos, string source)
    {
        uint rowFlag = lot.Get<uint>("getItemFlagId");
        for (int s = 1; s <= 8; s++)
        {
            int id = lot.Get<int>($"lotItemId0{s}");
            int cat = lot.Get<int>($"lotItemCategory0{s}");
            if (id <= 0 || lot.Get<int>($"lotItemBasePoint0{s}") == 0) continue;
            uint flag = rowFlag != 0 ? rowFlag : lot.Get<uint>($"getItemFlagId0{s}");
            var p = Classify(cat, id, flag, area, gx, gz, pos, source);
            if (p != null) Placements.Add(p);
        }
    }

    Placement Classify(int cat, int id, uint flag, byte area, byte gx, byte gz, Vector3 pos, string source)
    {
        switch (cat)
        {
            case 2: return Weapon(id, flag, area, gx, gz, pos, source);
            case 1: return Spell(id, flag, area, gx, gz, pos, source);
            case 3: return Armor(id, flag, area, gx, gz, pos, source);
            case 4: return Talisman(id, flag, area, gx, gz, pos, source);
            default: return null;
        }
    }

    // --- weapons -----------------------------------------------------------

    static readonly Dictionary<int, string> WepTypes = new()
    {
        [1] = "Dagger", [3] = "Straight Sword", [5] = "Greatsword", [7] = "Colossal Sword", [9] = "Curved Sword",
        [11] = "Curved Greatsword", [13] = "Katana", [14] = "Twinblade", [15] = "Thrusting Sword", [16] = "Heavy Thrusting Sword",
        [17] = "Axe", [19] = "Greataxe", [21] = "Hammer", [23] = "Great Hammer", [24] = "Flail", [25] = "Spear",
        [28] = "Great Spear", [29] = "Halberd", [31] = "Reaper", [35] = "Fist", [37] = "Claw", [39] = "Whip",
        [41] = "Colossal Weapon", [50] = "Light Bow", [51] = "Bow", [53] = "Greatbow", [55] = "Crossbow",
        [56] = "Ballista", [57] = "Glintstone Staff", [61] = "Sacred Seal", [65] = "Small Shield",
        [67] = "Medium Shield", [69] = "Greatshield", [87] = "Torch",
    };

    static string Grade(float v) => v >= 175 ? "S" : v >= 140 ? "A" : v >= 90 ? "B" : v >= 60 ? "C" : v >= 25 ? "D" : "E";

    Placement Weapon(int id, uint flag, byte area, byte gx, byte gz, Vector3 pos, string source)
    {
        int rowId = id - id % 100; // strip upgrade level, keep affinity
        var w = gd["EquipParamWeapon"][rowId];
        string name = text.Name(Kind.Weapon, rowId);
        if (w == null || name == null) { Skipped.Add($"weapon {id} no row/name"); return null; }
        int wepType = w.Get<int>("wepType");
        if (wepType >= 81 && wepType <= 86) return null; // ammo

        var corr = new (Stat s, float v, int req)[]
        {
            (Stat.STR, w.Get<float>("correctStrength"), w.Get<int>("properStrength")),
            (Stat.DEX, w.Get<float>("correctAgility"), w.Get<int>("properAgility")),
            (Stat.INT, w.Get<float>("correctMagic"), w.Get<int>("properMagic")),
            (Stat.FAI, w.Get<float>("correctFaith"), w.Get<int>("properFaith")),
            (Stat.ARC, w.Get<float>("correctLuck"), w.Get<int>("properLuck")),
        };
        // Every stat the weapon scales with at grade D or better; E is a token grade (e.g. STR E on staffs).
        var stats = corr.Where(c => c.v >= 25).Select(c => c.s).ToList();
        if (stats.Count == 0 && corr.Any(c => c.v > 0)) stats.Add(corr.MaxBy(c => c.v).s); // all-E weapons: best stat
        stats = tags.Override(name, stats); // may be empty: then it only shows under "All weapons"

        // Per stat: grade and requirement, e.g. "STR D 16". Shorter variants drop spacing, then E grades.
        var parts = corr.Where(c => c.v > 0 || c.req > 0).ToList();
        static string Stat1((Stat s, float v, int req) c, string sep) =>
            c.v > 0 ? (c.req > 0 ? $"{c.s} {Grade(c.v)}{sep}{c.req}" : $"{c.s} {Grade(c.v)}") : $"{c.s} {c.req}";
        var details = new[]
        {
            string.Join(", ", parts.Select(c => Stat1(c, " "))),
            string.Join(" ", parts.Select(c => Stat1(c, ""))),
            string.Join(" ", parts.Where(c => c.v >= 25).Select(c => Stat1(c, ""))),
        };
        return new Placement(Kind.Weapon, rowId, name, details, stats, flag, area, gx, gz, pos, source);
    }

    // --- spells ------------------------------------------------------------

    Placement Spell(int id, uint flag, byte area, byte gx, byte gz, Vector3 pos, string source)
    {
        // Spells are goods whose goodsType is a sorcery (5, 17) or incantation (16, 18) and that share
        // their id with a Magic row. Other goods can collide with a Magic id (Stonesword Key = 8000).
        var m = gd["Magic"][id];
        int goodsType = gd["EquipParamGoods"][id]?.Get<int>("goodsType") ?? -1;
        if (m == null || goodsType is not (5 or 16 or 17 or 18)) return null; // ordinary goods
        string name = text.Name(Kind.Spell, id);
        if (name == null) { Skipped.Add($"spell {id} no name"); return null; }
        var req = new (Stat s, int v)[]
        {
            (Stat.INT, m.Get<int>("requirementIntellect")),
            (Stat.FAI, m.Get<int>("requirementFaith")),
            (Stat.ARC, m.Get<int>("requirementLuck")),
        };
        var stats = tags.Override(name, req.Where(r => r.v > 0).Select(r => r.s).ToList());
        bool sorcery = goodsType is 5 or 17;
        string needs = string.Join(", ", req.Where(r => r.v > 0).Select(r => $"{r.s} {r.v}"));
        int fp = m.Get<int>("mp");
        var details = new[] { $"{needs}, {fp} FP", needs.Replace(", ", " ") + $" {fp}FP", needs.Replace(", ", " ") };
        return new Placement(Kind.Spell, id, name, details, stats, flag, area, gx, gz, pos, source);
    }

    // --- armor & talismans -------------------------------------------------

    Placement Armor(int id, uint flag, byte area, byte gx, byte gz, Vector3 pos, string source)
    {
        var a = gd["EquipParamProtector"][id];
        string name = text.Name(Kind.Armor, id);
        if (a == null || name == null) return null;
        // Build stats: hand tags plus any damage stat the piece actually raises.
        var bonuses = StatBonuses(a, "residentSpEffectId", "residentSpEffectId2", "residentSpEffectId3");
        var stats = tags.ArmorStats(name).Union(bonuses.Where(b => b.stat != null).Select(b => b.stat.Value)).ToList();
        // Untagged (plain) armor has no stats and only shows under "All armor".
        if (stats.Count == 0) UntaggedArmor.Add((name, $"m{area}_{gx:00}_{gz:00}", a.Get<float>("weight")));
        string slot = a.Get<int>("headEquip") == 1 ? "Head" : a.Get<int>("bodyEquip") == 1 ? "Chest" : a.Get<int>("armEquip") == 1 ? "Arms" : "Legs";
        // Real bonuses read "INT +3"; tag-only pieces read "INT build"; plain pieces just the slot.
        string what = bonuses.Count > 0 ? string.Join(", ", bonuses.Select(b => $"{b.label} {b.amount:+0;-0}"))
            : stats.Count > 0 ? $"{string.Join("/", stats)} build" : null;
        var details = what != null ? new[] { $"{slot}, {what}", what } : new[] { slot };
        return new Placement(Kind.Armor, id, name, details, stats, flag, area, gx, gz, pos, source);
    }

    static readonly (string field, string label, Stat? stat)[] BonusFields =
    {
        ("addLifeForceStatus", "VIG", null), ("addWillpowerStatus", "MND", null), ("addEndureStatus", "END", null),
        ("addStrengthStatus", "STR", Stat.STR), ("addDexterityStatus", "DEX", Stat.DEX), ("addMagicStatus", "INT", Stat.INT),
        ("addFaithStatus", "FAI", Stat.FAI), ("addLuckStatus", "ARC", Stat.ARC),
    };

    /// Attribute bonuses from an item's always-on SpEffects (e.g. Olivinus Glintstone Crown: INT +3).
    List<(string label, int amount, Stat? stat)> StatBonuses(PARAM.Row item, params string[] spFields)
    {
        var result = new List<(string, int, Stat?)>();
        if (!gd.Params.TryGetValue("SpEffectParam", out var sp)) return result;
        foreach (var f in spFields)
        {
            var e = sp[item.Get<int>(f)];
            if (e == null) continue;
            foreach (var (field, label, stat) in BonusFields)
                if (e.Get<int>(field) is int v && v != 0) result.Add((label, v, stat));
        }
        return result;
    }

    Placement Talisman(int id, uint flag, byte area, byte gx, byte gz, Vector3 pos, string source)
    {
        string name = text.Name(Kind.Talisman, id);
        if (name == null) return null;
        var stats = tags.TalismanStats(name); // may be empty: then it only shows under "All talismans"
        string info = text.TalismanInfo(id)?.Replace("\n", " ") ?? "";
        var details = new[] { info };
        return new Placement(Kind.Talisman, id, name, details, stats, flag, area, gx, gz, pos, source);
    }

    /// The map tooltip shows one line only and clips it at about 54 characters (newlines hide
    /// everything after the first line). Picks the most detailed variant that fits, then trims.
    public const int MaxHover = 50;

    public static string Fit(string name, string[] details, string suffix = "")
    {
        foreach (var d in details.Where(d => !string.IsNullOrEmpty(d)))
        {
            string s = $"{name} | {d}{suffix}";
            if (s.Length <= MaxHover) return s;
        }
        string last = details.LastOrDefault(d => !string.IsNullOrEmpty(d)) ?? "";
        string head = $"{name} | ";
        int room = MaxHover - head.Length - suffix.Length;
        if (room >= 8) return head + last.Substring(0, Math.Min(last.Length, room - 3)).TrimEnd(' ', ',') + "..." + suffix;
        return (name + suffix).Length <= MaxHover ? name + suffix : name.Substring(0, MaxHover - suffix.Length - 3) + "..." + suffix;
    }
}

/// English item names and info, merged across base and DLC FMGs.
class Text
{
    readonly Dictionary<string, Dictionary<int, string>> fmgs = new();

    public Text(string itemBndPath)
    {
        var bnd = BND4.Read(itemBndPath);
        foreach (var f in bnd.Files)
        {
            string n = Path.GetFileNameWithoutExtension(f.Name);
            string baseName = n.Replace("_dlc01", "").Replace("_dlc02", "");
            if (!fmgs.TryGetValue(baseName, out var d)) fmgs[baseName] = d = new();
            foreach (var e in FMG.Read(f.Bytes).Entries)
                if (!string.IsNullOrWhiteSpace(e.Text) && e.Text != "[ERROR]") d[e.ID] = e.Text;
        }
    }

    public IEnumerable<(string fmg, int id, string name)> All() =>
        fmgs.Where(kv => kv.Key.EndsWith("Name")).SelectMany(kv => kv.Value.Select(e => (kv.Key, e.Key, e.Value)));

    public string Get(string fmg, int id) => fmgs.TryGetValue(fmg, out var d) && d.TryGetValue(id, out var s) ? s : null;

    public string Name(Kind k, int id) => k switch
    {
        Kind.Weapon => Get("WeaponName", id),
        Kind.Spell => Get("GoodsName", id),
        Kind.Armor => Get("ProtectorName", id),
        Kind.Talisman => Get("AccessoryName", id),
        _ => null,
    };

    public string TalismanInfo(int id) => Get("AccessoryInfo", id);
}

/// Hand tags from data/tags.json.
class Tags
{
    readonly Dictionary<string, List<Stat>> talismans, armorSets, armorKeywords;
    readonly HashSet<string> arcaneSpells;

    public Tags(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        talismans = Map(root.GetProperty("talismans"));
        armorSets = Map(root.GetProperty("armorSets"));
        armorKeywords = Map(root.GetProperty("armorKeywords"));
        arcaneSpells = root.GetProperty("spellArcaneOverride").EnumerateArray().Select(e => e.GetString()).ToHashSet();
    }

    static Dictionary<string, List<Stat>> Map(JsonElement obj) => obj.EnumerateObject()
        .Where(p => !p.Name.StartsWith("_"))
        .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => Enum.Parse<Stat>(v.GetString())).ToList());

    public List<Stat> TalismanStats(string name) => talismans.GetValueOrDefault(name) ?? new();

    public List<Stat> ArmorStats(string name) =>
        armorSets.Where(kv => name.StartsWith(kv.Key)).OrderByDescending(kv => kv.Key.Length).Select(kv => kv.Value).FirstOrDefault()
        ?? armorKeywords.Where(kv => name.Contains(kv.Key)).Select(kv => kv.Value).FirstOrDefault()
        ?? new();

    public List<Stat> Override(string name, List<Stat> stats)
    {
        if (arcaneSpells.Contains(name) && !stats.Contains(Stat.ARC)) stats.Add(Stat.ARC);
        return stats;
    }
}
