using SoulsFormats;

namespace BuildCompass.Gen;

/// Item lots handed out by event scripts when an enemy dies (boss rewards and the like), with
/// the enemy they're tied to and the name its health bar shows.
///
/// Rewards come from AwardItemLot instructions, usually inside a common event whose lot id and
/// enemy arrive as event arguments, so every call site (InitializeEvent / InitializeCommonEvent)
/// is resolved separately. Some rewards only watch a flag (e.g. common event 1200: "boss flag
/// set -> award lot"); the enemy is then found in whichever event sets that flag.
class Rewards
{
    public record Enemy(string Map, MSBE.Part.Enemy Part, int NpcId);
    /// A lootable object (a corpse the player searches), for rewards that no enemy death gives.
    public record Prop(string Map, string Name, System.Numerics.Vector3 Position);
    public record Reward(int LotId, Enemy Enemy, Prop Corpse, bool Boss, int NameId, string Source);

    // AwardItemLot, AwardItemsIncludingClients.
    static readonly HashSet<(int, int)> AwardInstrs = new() { (2003, 4), (2003, 36) };
    static readonly (int, int) BossDefeat = (2003, 12);   // HandleBossDefeatAndDisplayBanner(entity, banner)
    static readonly (int, int) HealthBar = (2003, 11);    // DisplayBossHealthBar(state, entity, slot, nameId)
    static readonly (int, int) SetFlag = (2003, 66);      // SetEventFlag(type, flag, state)
    static readonly HashSet<(int, int)> InitInstrs = new() { (2000, 0), (2000, 6) }; // [slot][event id][args...]
    static readonly (int, int) ActionButton = (3, 24);    // IfActionButtonInArea(cond, param, entity): the thing searched
    // Entity ids have at least this many digits; smaller values in an event are flags and offsets.
    const int MinEntityId = 1_000_000;

    public readonly List<Reward> All = new();

    record Ev(string File, EMEVD.Event E);

    readonly Dictionary<long, Ev> events = new();
    readonly Dictionary<long, List<byte[]>> calls = new();

    public Rewards(Dictionary<uint, Enemy> enemies, Dictionary<uint, Prop> props)
    {
        var all = new List<Ev>();
        foreach (var path in Program.InFiles("*.emevd.dcx", "event"))
        {
            string file = Path.GetFileName(path).Split('.')[0];
            foreach (var e in EMEVD.Read(path).Events) { all.Add(new Ev(file, e)); events.TryAdd(e.ID, new Ev(file, e)); }
        }
        // Every file's event 0 starts that map's events, so call sites come from all of them.
        foreach (var ev in all)
            foreach (var ins in ev.E.Instructions)
                if (InitInstrs.Contains((ins.Bank, ins.ID)) && ins.ArgData.Length >= 8)
                {
                    long target = BitConverter.ToUInt32(ins.ArgData, 4);
                    if (!calls.TryGetValue(target, out var list)) calls[target] = list = new();
                    list.Add(ins.ArgData[8..]);
                }

        // Boss names: health bars, entity -> name id.
        var names = new Dictionary<int, int>();
        foreach (var ev in events.Values)
            foreach (var (ins, i) in Instrs(ev, HealthBar))
                foreach (var args in Sites(ev))
                    if (Arg(ev, i, 4, args) is int entity && Arg(ev, i, 12, args) is int name && name > 0) names.TryAdd(entity, name);

        // Who sets which flag: flag -> (event, its call args).
        var setters = new Dictionary<int, List<(Ev ev, byte[] args)>>();
        foreach (var ev in events.Values)
            foreach (var (ins, i) in Instrs(ev, SetFlag))
                foreach (var args in Sites(ev))
                    if (Arg(ev, i, 4, args) is int flag && flag > 0)
                        (setters.TryGetValue(flag, out var l) ? l : setters[flag] = new()).Add((ev, args));

        var seen = new HashSet<(int, uint)>();
        foreach (var ev in events.Values)
            foreach (var (ins, i) in AwardInstrs.SelectMany(a => Instrs(ev, a)))
                foreach (var args in Sites(ev))
                {
                    if (Arg(ev, i, 0, args) is not int lot || lot <= 0) continue;
                    // Searched loot: the event names exactly what is searched, an enemy's corpse or a corpse object.
                    var searched = Instrs(ev, ActionButton).Select(b => Arg(ev, b.index, 8, args)).FirstOrDefault(e => e > 0);
                    if (searched is int s && !enemies.ContainsKey((uint)s) && props.TryGetValue((uint)s, out var prop))
                    {
                        if (seen.Add((lot, (uint)s))) All.Add(new Reward(lot, null, prop, false, 0, $"{ev.File} ev {ev.E.ID}"));
                        continue;
                    }
                    // The enemy: a value in this event or its call; failing that, the event setting a flag it watches.
                    var direct = (searched is int only ? new[] { only }.AsEnumerable() : Values(ev, args)).ToList();
                    var found = Find(ev, args, direct);
                    if (found == null)
                        foreach (var v in direct)
                            foreach (var (sev, sargs) in setters.GetValueOrDefault(v) ?? new())
                                if ((found = Find(sev, sargs, Values(sev, sargs).ToList())) != null) goto done;
                    done:
                    if (found == null) continue;
                    var (enemy, boss, entity) = found.Value;
                    if (!seen.Add((lot, enemy.Part.EntityID))) continue;
                    All.Add(new Reward(lot, enemy, null, boss, names.GetValueOrDefault(entity), $"{ev.File} ev {ev.E.ID}"));
                }

        (Enemy enemy, bool boss, int entity)? Find(Ev ev, byte[] args, List<int> values)
        {
            foreach (var v in values.Where(v => v >= MinEntityId))
                if (enemies.TryGetValue((uint)v, out var enemy))
                    return (enemy, Instrs(ev, BossDefeat).Any(), v);
            return null;
        }
    }

    static IEnumerable<(EMEVD.Instruction ins, int index)> Instrs(Ev ev, (int, int) which) =>
        ev.E.Instructions.Select((ins, i) => (ins, i)).Where(x => (x.ins.Bank, x.ins.ID) == which);

    /// The argument blocks this event is started with (one empty block if nothing starts it, so
    /// events with only literal values still count once).
    IEnumerable<byte[]> Sites(Ev ev) => calls.TryGetValue(ev.E.ID, out var l) && l.Count > 0 ? l : new List<byte[]> { Array.Empty<byte>() };

    /// One instruction argument (4 bytes at `offset`): its literal value, or the value the call
    /// passes in when the event substitutes a parameter there. Null if the call doesn't supply it.
    static int? Arg(Ev ev, int index, int offset, byte[] args)
    {
        var ins = ev.E.Instructions[index];
        var p = ev.E.Parameters.FirstOrDefault(p => p.InstructionIndex == index && p.TargetStartByte == offset);
        if (p == null) return offset + 4 <= ins.ArgData.Length ? BitConverter.ToInt32(ins.ArgData, offset) : null;
        return p.SourceStartByte + 4 <= args.Length ? BitConverter.ToInt32(args, (int)p.SourceStartByte) : null;
    }

    /// Every 4-byte value the call passes in, then every literal in the event.
    static IEnumerable<int> Values(Ev ev, byte[] args) =>
        Ints(args).Concat(ev.E.Instructions.SelectMany(ins => Ints(ins.ArgData)));

    static IEnumerable<int> Ints(byte[] data)
    {
        for (int o = 0; o + 4 <= data.Length; o += 4) yield return BitConverter.ToInt32(data, o);
    }

    /// Every enemy part in the game's maps by entity id, and by entity group id (the group's first
    /// member, e.g. a trio boss), with its NpcParam id.
    public static (Dictionary<uint, Enemy> enemies, Dictionary<uint, Prop> props) LoadParts()
    {
        var byId = new Dictionary<uint, Enemy>();
        var byGroup = new Dictionary<uint, Enemy>();
        var props = new Dictionary<uint, Prop>();
        foreach (var path in Program.InFiles("*.msb.dcx", "map", "mapstudio"))
        {
            string map = Path.GetFileName(path).Split('.')[0];
            if (map.EndsWith("_99")) continue;
            var msb = MSBE.Read(path);
            foreach (var a in msb.Parts.Assets)
                if (a.EntityID != 0) props.TryAdd(a.EntityID, new Prop(map, a.Name, a.Position));
            foreach (var e in msb.Parts.Enemies)
            {
                var enemy = new Enemy(map, e, e.NPCParamID);
                if (e.EntityID != 0) byId.TryAdd(e.EntityID, enemy);
                foreach (var g in e.EntityGroupIDs.Where(g => g != 0)) byGroup.TryAdd(g, enemy);
            }
        }
        foreach (var (g, e) in byGroup) byId.TryAdd(g, e);
        return (byId, props);
    }
}
