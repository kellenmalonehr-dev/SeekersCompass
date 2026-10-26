namespace BuildCompass.Gen;

/// Checks the stat-bonus fields of armor resident SpEffects.
static class ArmorBonus
{
    public static void Run()
    {
        var gd = GameData.Load(Path.Combine(Program.GameDir, "regulation.bin"));
        var text = new Text(Path.Combine(Program.GameDir, "msg", "engus", "item_dlc02.msgbnd.dcx"));
        var sp = gd["SpEffectParam"];
        var names = sp.AppliedParamdef.Fields.Select(f => f.InternalName).ToHashSet();
        Console.WriteLine("has stat fields: " + string.Join(",", new[] { "addStrengthStatus", "addDexterityStatus", "addMagicStatus", "addFaithStatus", "addLuckStatus" }.Select(n => $"{n}={names.Contains(n)}")));
        foreach (var nm in new[] { "Olivinus Glintstone Crown", "Lazuli Glintstone Crown", "Witch's Glintstone Crown", "Hierodas Glintstone Crown", "Radagon's Soreseal" })
        {
            var armor = gd["EquipParamProtector"].Rows.FirstOrDefault(r => text.Get("ProtectorName", r.ID) == nm);
            var acc = gd["EquipParamAccessory"].Rows.FirstOrDefault(r => text.Get("AccessoryName", r.ID) == nm);
            var row = armor ?? acc; if (row == null) { Console.WriteLine($"{nm}: not found"); continue; }
            var ids = row.Cells.Where(c => c.Def.InternalName.StartsWith("residentSpEffectId")).Select(c => Convert.ToInt32(c.Value)).Where(i => i > 0);
            foreach (int id in ids)
            {
                var s = sp[id];
                Console.WriteLine($"{nm}: sp {id} " + (s == null ? "missing" : string.Join(" ", new[] { "addStrengthStatus", "addDexterityStatus", "addMagicStatus", "addFaithStatus", "addLuckStatus", "addLifeForceStatus", "addWillpowerStatus", "addEndureStatus" }.Select(n => $"{n.Replace("add", "").Replace("Status", "")}={s[n].Value}"))));
            }
        }
    }
}
