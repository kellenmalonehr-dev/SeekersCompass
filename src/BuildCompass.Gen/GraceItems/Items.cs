namespace GraceItems.Gen;

/// The parts of grace_items.txt the menu needs. The DLL reads the same file (items.rs), and both
/// number items by their position in it, so the menu's flags line up with the DLL's.
record Item(string Id, string Name, bool Seat, string Effect, int Max, float Minutes)
{
    public static List<Item> Load(string path)
    {
        var items = new List<Item>();
        string id = null;
        var fields = new Dictionary<string, string>();
        void Flush()
        {
            if (id == null) return;
            bool seat = fields.GetValueOrDefault("kind", "keepsake").Equals("seat", StringComparison.OrdinalIgnoreCase);
            int.TryParse(fields.GetValueOrDefault("max", "0"), out int max);
            float.TryParse(fields.GetValueOrDefault("minutes", "15"), System.Globalization.CultureInfo.InvariantCulture, out float minutes);
            items.Add(new Item(id, fields.GetValueOrDefault("name", id), seat, new string(fields.GetValueOrDefault("effect", "").Where(char.IsLetter).ToArray()), max, minutes));
        }
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Split(';', '#')[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                id = line[1..^1].Trim();
                fields.Clear();
            }
            else if (line.Split('=', 2) is [var k, var v])
                fields[k.Trim().ToLowerInvariant()] = v.Trim();
        }
        Flush();
        return items;
    }
}
