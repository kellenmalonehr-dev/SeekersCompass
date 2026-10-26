using SoulsFormats;

namespace BuildCompass.Gen;

/// Confirms SoulsFormats round-trips the vanilla grace ESD byte-for-byte.
static class EsdCheck
{
    public static void Run()
    {
        var bnd = BND4.Read(Path.Combine(Program.GameDir, "script", "talk", "m00_00_00_00.talkesdbnd.dcx"));
        foreach (var f in bnd.Files)
        {
            var esd = ESD.Read(f.Bytes);
            var again = esd.Write();
            bool same = again.AsSpan().SequenceEqual(f.Bytes);
            Console.WriteLine($"{Path.GetFileName(f.Name)}: {f.Bytes.Length} -> {again.Length} {(same ? "identical" : "DIFFERENT")}");
            if (!same) Console.WriteLine($"   reparse ok: {ESD.Read(again).StateGroups.Count == esd.StateGroups.Count}");
        }
        Console.WriteLine($"bnd compression {bnd.Compression}, format {bnd.Format}");
    }
}

static class EsdCompare
{
    /// Dumps vanilla and re-saved t000001000 the same way and compares them line by line.
    public static void Run()
    {
        var bnd = BND4.Read(Path.Combine(Program.GameDir, "script", "talk", "m00_00_00_00.talkesdbnd.dcx"));
        var f = bnd.Files.First(x => x.Name.EndsWith("t000001000.esd"));
        var a = ESD.Read(f.Bytes);
        var b = ESD.Read(a.Write());
        var da = Dump(a); var db = Dump(b);
        Console.WriteLine($"lines {da.Count} vs {db.Count}; differing: {da.Zip(db).Count(p => p.First != p.Second)}");
        Console.WriteLine($"name '{a.Name}' vs '{b.Name}'; unk {a.Unk70},{a.Unk74},{a.Unk78},{a.Unk7C} vs {b.Unk70},{b.Unk74},{b.Unk78},{b.Unk7C}");
    }

    static List<string> Dump(ESD e)
    {
        var lines = new List<string>();
        string Cmd(ESD.CommandCall c) => $"{c.CommandBank}_{c.CommandID}(" + string.Join(",", c.Arguments.Select(x => Convert.ToHexString(x))) + ")";
        void Cond(ESD.Condition c, string ind) { lines.Add($"{ind}{Convert.ToHexString(c.Evaluator)}->{c.TargetState}"); foreach (var p in c.PassCommands) lines.Add(ind + "p" + Cmd(p)); foreach (var s in c.Subconditions) Cond(s, ind + " "); }
        foreach (var (g, states) in e.StateGroups.OrderBy(x => x.Key))
            foreach (var (id, s) in states.OrderBy(x => x.Key))
            {
                lines.Add($"{g}/{id}");
                foreach (var c in s.EntryCommands) lines.Add("e" + Cmd(c));
                foreach (var c in s.ExitCommands) lines.Add("x" + Cmd(c));
                foreach (var c in s.WhileCommands) lines.Add("w" + Cmd(c));
                foreach (var c in s.Conditions) Cond(c, " ");
            }
        return lines;
    }
}
