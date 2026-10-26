using SoulsFormats;

namespace BuildCompass.Gen;

/// Collects every value vanilla might use as an event flag. Over-collecting is fine:
/// anything in this set is treated as taken.
static class FlagScan
{
    public static HashSet<uint> UsedFlags(GameData gd)
    {
        var used = new HashSet<uint>();
        foreach (var path in Program.InFiles("*.emevd.dcx", "event"))
            foreach (var ev in EMEVD.Read(path).Events)
            {
                used.Add((uint)ev.ID);
                foreach (var ins in ev.Instructions)
                    for (int i = 0; i + 4 <= ins.ArgData.Length; i += 4)
                        used.Add(BitConverter.ToUInt32(ins.ArgData, i));
            }
        // Talk scripts set and test flags too (vanilla and any base layer): take every int32 literal.
        foreach (var path in Program.InFiles("*.talkesdbnd.dcx", "script", "talk"))
            foreach (var f in BND4.Read(path).Files.Where(f => f.Name.EndsWith(".esd")))
                foreach (var states in ESD.Read(f.Bytes).StateGroups.Values)
                    foreach (var s in states.Values)
                        foreach (var code in Bytecode(s)) AddInt32s(code, used);
        foreach (var p in gd.Params.Values)
        {
            var fields = p.AppliedParamdef.Fields.Where(f => f.InternalName.Contains("flag", StringComparison.OrdinalIgnoreCase)).Select(f => f.InternalName).ToList();
            foreach (var row in p.Rows)
                foreach (var f in fields)
                    if (row[f].Value is uint u) used.Add(u);
                    else if (row[f].Value is int i && i > 0) used.Add((uint)i);
        }
        return used;
    }

    static IEnumerable<byte[]> Bytecode(ESD.State s)
    {
        IEnumerable<ESD.CommandCall> cmds = s.EntryCommands.Concat(s.ExitCommands).Concat(s.WhileCommands);
        var conds = new Stack<ESD.Condition>(s.Conditions);
        while (conds.Count > 0)
        {
            var c = conds.Pop();
            yield return c.Evaluator;
            cmds = cmds.Concat(c.PassCommands);
            foreach (var sc in c.Subconditions) conds.Push(sc);
        }
        foreach (var c in cmds) foreach (var a in c.Arguments) yield return a;
    }

    /// EzState pushes 32-bit literals as 0x82 + int32.
    static void AddInt32s(byte[] code, HashSet<uint> used)
    {
        for (int i = 0; i + 5 <= code.Length; i++)
            if (code[i] == 0x82) used.Add(BitConverter.ToUInt32(code, i + 1));
    }

    public static void Report(string[] args)
    {
        var gd = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"));
        var used = UsedFlags(gd);
        var blocks = used.Where(f => f < 100000).GroupBy(f => f / 1000).OrderBy(b => b.Key).ToList();
        Console.WriteLine("global blocks (id/1000: refs): " + string.Join(" ", blocks.Select(b => $"{b.Key}:{b.Count()}")));
        string prefix = args.Length > 0 ? args[0] : "104236";
        var tile = used.Where(f => f.ToString().StartsWith(prefix) && f.ToString().Length == 10).GroupBy(f => f / 1000).OrderBy(b => b.Key);
        foreach (var b in tile)
            Console.WriteLine($"  {b.Key}xxx: {b.Count()} refs, e.g. {string.Join(",", b.OrderBy(x => x).Take(8))}");
    }
}

