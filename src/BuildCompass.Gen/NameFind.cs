namespace BuildCompass.Gen;

/// `namefind <text>`: item names (all kinds, base + DLC) containing the text.
static class NameFind
{
    public static void Run(string[] args)
    {
        var text = new Text(Path.Combine(Program.GameDir, "msg", "engus", "item_dlc02.msgbnd.dcx"));
        string q = string.Join(" ", args);
        foreach (var (fmg, id, name) in text.All().Where(x => x.name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"{fmg} {id}: {name}");
    }
}
