using System.Numerics;
using SoulsFormats;

namespace BuildCompass.Gen;

/// `arenas`: every boss fight in the game's event scripts with its map, its defeat flag and the
/// nearest Site of Grace, to see which boss rooms could serve as Roundtable hubs (see Hubs).
/// A fight is an event that calls HandleBossDefeatAndDisplayBanner; its flag is the first one that
/// event sets. Output is tab-separated: kind, boss, map, flag, metres to the grace, grace, grace
/// entity, height difference, whether the map's talk file is unpacked.
static class Arenas
{
    static readonly (int, int) BossDefeat = (2003, 12);   // HandleBossDefeatAndDisplayBanner(entity, banner)
    static readonly (int, int) HealthBar = (2003, 11);    // DisplayBossHealthBar(state, entity, slot, nameId)
    static readonly (int, int) SetFlag = (2003, 66);      // SetEventFlag(type, flag, state)

    record Ev(string File, EMEVD.Event E);

    public static void Run()
    {
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        var (enemies, props) = Rewards.LoadParts();

        var events = new List<Ev>();
        var calls = new Dictionary<long, List<byte[]>>();
        foreach (var path in Program.InFiles("*.emevd.dcx", "event"))
        {
            string file = Path.GetFileName(path).Split('.')[0];
            foreach (var e in EMEVD.Read(path).Events)
            {
                events.Add(new Ev(file, e));
                foreach (var ins in e.Instructions)
                    if (ins.Bank == 2000 && (ins.ID == 0 || ins.ID == 6) && ins.ArgData.Length >= 8)
                    {
                        long target = BitConverter.ToUInt32(ins.ArgData, 4);
                        (calls.TryGetValue(target, out var l) ? l : calls[target] = new()).Add(ins.ArgData[8..]);
                    }
            }
        }
        IEnumerable<byte[]> Sites(Ev ev) => calls.TryGetValue(ev.E.ID, out var l) && l.Count > 0 ? l : new List<byte[]> { Array.Empty<byte>() };
        static int? Arg(Ev ev, int index, int offset, byte[] args)
        {
            var ins = ev.E.Instructions[index];
            var p = ev.E.Parameters.FirstOrDefault(p => p.InstructionIndex == index && p.TargetStartByte == offset);
            if (p == null) return offset + 4 <= ins.ArgData.Length ? BitConverter.ToInt32(ins.ArgData, offset) : null;
            return p.SourceStartByte + 4 <= args.Length ? BitConverter.ToInt32(args, (int)p.SourceStartByte) : null;
        }
        static IEnumerable<int> Instrs(Ev ev, (int, int) which) =>
            Enumerable.Range(0, ev.E.Instructions.Count).Where(i => (ev.E.Instructions[i].Bank, ev.E.Instructions[i].ID) == which);

        var names = new Dictionary<int, int>();
        foreach (var ev in events)
            foreach (int i in Instrs(ev, HealthBar))
                foreach (var args in Sites(ev))
                    if (Arg(ev, i, 4, args) is int entity && Arg(ev, i, 12, args) is int name && name > 0) names.TryAdd(entity, name);

        // Graces: where each one stands (its own object in the map) and what it's called.
        var graces = new List<(string map, Vector3 pos, string name, uint entity)>();
        foreach (var r in gd["BonfireWarpParam"].Rows)
        {
            uint entity = r.Get<uint>("bonfireEntityId");
            if (entity != 0 && props.TryGetValue(entity, out var prop))
                graces.Add((prop.Map, prop.Position, text.Get("PlaceName", r.Get<int>("textId1")) ?? $"grace {r.ID}", entity));
        }
        Console.Error.WriteLine($"{graces.Count} graces placed of {gd["BonfireWarpParam"].Rows.Count}");
        File.WriteAllLines(Path.Combine(Program.Root, "work", "graces.tsv"), graces.OrderBy(g => g.map).Select(g => $"{g.map}	{g.entity}	{g.name}	{g.pos.X:0.0} {g.pos.Y:0.0} {g.pos.Z:0.0}"));

        // Open-world tiles share one space; _01 and _02 tiles are 2 and 4 tiles wide.
        static (string space, Vector3 pos) World(string map, Vector3 local)
        {
            var p = map[1..].Split('_').Select(int.Parse).ToArray();
            if (p[0] is not (60 or 61)) return (map, local);
            int scale = 256 << p[3] % 10;
            return ($"m{p[0]}", new Vector3(p[1] * scale + local.X, local.Y, p[2] * scale + local.Z));
        }

        var talk = Directory.GetFiles(Path.Combine(Program.GameDir, "script", "talk"), "*.talkesdbnd.dcx").Select(f => Path.GetFileName(f).Split('.')[0]).ToHashSet();
        var npc = gd["NpcParam"];
        var seen = new HashSet<int>();
        var lines = new List<string>();
        foreach (var ev in events)
            foreach (int i in Instrs(ev, BossDefeat))
                foreach (var args in Sites(ev))
                {
                    if (Arg(ev, i, 0, args) is not int entity || !enemies.TryGetValue((uint)entity, out var enemy)) continue;
                    int flag = Instrs(ev, SetFlag).Select(s => Arg(ev, s, 4, args) ?? 0).FirstOrDefault(f => f > 0);
                    if (!seen.Add(flag != 0 ? flag : entity)) continue;
                    string who = (names.TryGetValue(entity, out int n) ? text.Get("NpcName", n) : null)
                        ?? (npc[enemy.NpcId] is { } row ? text.Get("NpcName", row.Get<int>("nameId")) : null) ?? $"npc {enemy.NpcId}";
                    var me = World(enemy.Map, enemy.Part.Position);
                    var near = graces.Select(g => (g, w: World(g.map, g.pos))).Where(x => x.w.space == me.space)
                        .Select(x => (x.g, d: Vector3.Distance(x.w.pos, me.pos), dy: x.w.pos.Y - me.pos.Y)).OrderBy(x => x.d).FirstOrDefault();
                    int area = int.Parse(enemy.Map[1..3]);
                    string kind = area is 60 or 61 ? "open world" : area >= 30 && area < 60 ? "dungeon" : "legacy";
                    lines.Add(string.Join('\t', kind, who, enemy.Map, flag,
                        near.g.name != null ? $"{near.d:0}" : "", near.g.name ?? "(no grace in this map)", near.g.name != null ? near.g.entity : 0,
                        near.g.name != null ? $"{near.dy:0}" : "", talk.Contains(enemy.Map) ? "talk unpacked" : "talk packed"));
                }
        foreach (string l in lines.OrderBy(l => l)) Console.WriteLine(l);
        Console.Error.WriteLine($"{lines.Count} boss fights");
    }
}
