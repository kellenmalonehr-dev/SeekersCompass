using SoulsFormats;

namespace BuildCompass.Gen;

/// `speffects` : how many SpEffectParam rows of each stateInfo change HP.
/// `speffects <stateInfo> [stateInfo...]`: those rows, with the params and fields that refer to each.
/// `speffects free <from> <to>`: rows in the range that nothing refers to.
/// Used to tell the status rows the player's attacks apply from the ones enemies' attacks apply.
static class SpEffectScan
{
    public static void Run(string[] args)
    {
        var gd = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"));
        var sp = gd["SpEffectParam"];
        if (args.Length == 0)
        {
            foreach (var g in sp.Rows.Where(Changes).GroupBy(r => r.Get<int>("stateInfo")).OrderBy(g => g.Key))
                Console.WriteLine($"stateInfo {g.Key,-4} {g.Count(),-5} rows, e.g. {string.Join(", ", g.Take(8).Select(r => r.ID))}");
            return;
        }

        // Every field in any param that names a special effect, by the id it holds.
        var uses = new Dictionary<int, Dictionary<string, int>>();
        foreach (var (name, param) in gd.Params)
        {
            if (param.Rows.Count == 0) continue;
            var fields = param.Rows[0].Cells.Select(c => c.Def.InternalName)
                .Where(f => f.Contains("spEffect", StringComparison.OrdinalIgnoreCase) || f.Contains("SpEffectId", StringComparison.OrdinalIgnoreCase)
                    || (name == "SpEffectParam" && (f.EndsWith("Id") || f.EndsWith("ID")))).ToList();
            foreach (var row in param.Rows)
                foreach (var f in fields)
                {
                    int id;
                    try { id = Convert.ToInt32(row[f].Value); } catch { continue; }
                    if (id <= 0) continue;
                    if (!uses.TryGetValue(id, out var by)) uses[id] = by = new();
                    string key = $"{name}.{f}";
                    by[key] = by.GetValueOrDefault(key) + 1;
                }
        }

        if (args[0] == "free")
        {
            int from = int.Parse(args[1]), to = int.Parse(args[2]);
            foreach (var r in sp.Rows.Where(r => r.ID >= from && r.ID <= to && !uses.ContainsKey(r.ID)))
                Console.WriteLine($"{r.ID} unused: maxHpRate {r["maxHpRate"].Value} physAtkPowerRate {r["physicsAttackPowerRate"].Value} stateInfo {r["stateInfo"].Value} duration {r["effectEndurance"].Value}");
            return;
        }

        var wanted = args.Select(int.Parse).ToHashSet();
        foreach (var r in sp.Rows.Where(r => wanted.Contains(r.Get<int>("stateInfo")) && Changes(r)))
        {
            string by = uses.TryGetValue(r.ID, out var u) ? string.Join(", ", u.Select(kv => $"{kv.Key} x{kv.Value}")) : "(nothing)";
            Console.WriteLine($"{r.ID,-9} state {r["stateInfo"].Value,-4} hpRate {r["changeHpRate"].Value,-6} hpPoint {r["changeHpPoint"].Value,-6} <- {by}");
        }
    }

    static bool Changes(PARAM.Row r) => r.Get<float>("changeHpRate") > 0 || r.Get<int>("changeHpPoint") > 0;
}

/// `fielduse <param> <field> <neutral>`: rows whose field differs from the neutral value.
static class FieldUse
{
    public static void Run(string[] args)
    {
        var p = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"))[args[0]];
        var rows = p.Rows.Where(r => Convert.ToString(r[args[1]].Value) != args[2]).ToList();
        Console.WriteLine($"{args[1]}: {rows.Count} rows differ from {args[2]}");
        foreach (var g in rows.GroupBy(r => Convert.ToString(r[args[1]].Value)).OrderByDescending(g => g.Count()).Take(12))
            Console.WriteLine($"  {g.Key,-8} x{g.Count(),-5} e.g. {string.Join(", ", g.Take(8).Select(r => r.ID))}");
    }
}
