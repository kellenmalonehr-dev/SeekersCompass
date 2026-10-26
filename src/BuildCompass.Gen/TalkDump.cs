using SoulsFormats;

namespace BuildCompass.Gen;

/// `talk [esdName] [group]`: lists talk scripts, or dumps one script's state machine.
static class TalkDump
{
    public static void Run(string[] args)
    {
        bool fromMod = args.Contains("--mod");
        args = args.Where(a => a != "--mod").ToArray();
        int bi = Array.IndexOf(args, "--base"); string baseArg = bi >= 0 ? args[bi + 1] : null;
        if (bi >= 0) args = args.Where((a, i) => i != bi && i != bi + 1).ToArray();
        string baseDir = baseArg ?? (fromMod ? Path.Combine(Program.Root, "mod") : Program.GameDir);
        var bnd = BND4.Read(Path.Combine(baseDir, "script", "talk", "m00_00_00_00.talkesdbnd.dcx"));
        if (args.Length == 0)
        {
            foreach (var f in bnd.Files) Console.WriteLine($"{f.ID} {f.Name} {f.Bytes.Length}B");
            return;
        }
        var file = bnd.Files.First(f => f.Name.Contains(args[0]));
        var esd = ESD.Read(file.Bytes);
        Console.WriteLine($"{file.Name}: long={esd.LongFormat} ds={esd.DarkSoulsCount} groups={esd.StateGroups.Count}");
        foreach (var (gid, states) in esd.StateGroups.OrderBy(g => g.Key))
        {
            if (args.Length > 1 && gid.ToString() != args[1]) { Console.WriteLine($"group {gid}: {states.Count} states"); continue; }
            Console.WriteLine($"== group {gid}: {states.Count} states");
            foreach (var (sid, s) in states.OrderBy(x => x.Key))
            {
                Console.WriteLine($"  state {sid}");
                foreach (var c in s.EntryCommands) Console.WriteLine($"    entry {Cmd(c)}");
                foreach (var c in s.WhileCommands) Console.WriteLine($"    while {Cmd(c)}");
                foreach (var c in s.ExitCommands) Console.WriteLine($"    exit  {Cmd(c)}");
                foreach (var c in s.Conditions) Cond(c, "    ");
            }
        }
    }

    static string Cmd(ESD.CommandCall c) => $"c{c.CommandBank}_{c.CommandID}({string.Join(", ", c.Arguments.Select(EzState.Disasm))})";

    static void Cond(ESD.Condition c, string indent)
    {
        Console.WriteLine($"{indent}if [{EzState.Disasm(c.Evaluator)}] -> {c.TargetState?.ToString() ?? "-"}");
        foreach (var p in c.PassCommands) Console.WriteLine($"{indent}  pass {Cmd(p)}");
        foreach (var sc in c.Subconditions) Cond(sc, indent + "  ");
    }
}
