namespace BuildCompass.Gen;

/// `graces [name part]`: every site of grace with its entity id (what grace_items.txt's "found"
/// takes), map area and name.
static class Graces
{
    public static void Run(string[] args)
    {
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        string part = string.Join(" ", args);
        foreach (var r in gd["BonfireWarpParam"].Rows.OrderBy(r => r.ID))
        {
            string name = text.Get("PlaceName", r.Get<int>("textId1"));
            if (name == null || !name.Contains(part, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{r["bonfireEntityId"].Value,-11} row {r.ID,-8} area {r["areaNo"].Value,-3} {name}");
        }
    }
}
