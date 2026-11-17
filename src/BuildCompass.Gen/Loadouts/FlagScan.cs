using SoulsFormats;

namespace GraceLoadouts.Gen;

/// Collects every value the game (or its base layer) might use as an event flag. Over-collecting is fine:
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

    /// Prints runs of unreferenced flags in one block: freeflags [--base <dir>] [block, default 78].
    public static void ReportFree(string[] args)
    {
        int bi = Array.IndexOf(args, "--base");
        if (bi >= 0) Program.Base = args[bi + 1];
        uint block = uint.Parse(args.Where((a, i) => bi < 0 || (i != bi && i != bi + 1)).FirstOrDefault() ?? "78");
        var used = UsedFlags(GameData.Load(Program.In("regulation.bin")));
        var runs = new List<(uint from, uint to)>();
        foreach (var f in Enumerable.Range(0, 1000).Select(i => block * 1000 + (uint)i).Where(f => !used.Contains(f)))
            if (runs.Count > 0 && runs[^1].to == f - 1) runs[^1] = (runs[^1].from, f);
            else runs.Add((f, f));
        Console.WriteLine(string.Join(" ", runs.Select(r => r.from == r.to ? $"{r.from}" : $"{r.from}-{r.to}")));
    }

    /// EzState pushes 32-bit literals as 0x82 + int32.
    static void AddInt32s(byte[] code, HashSet<uint> used)
    {
        for (int i = 0; i + 5 <= code.Length; i++)
            if (code[i] == 0x82) used.Add(BitConverter.ToUInt32(code, i + 1));
    }
}
