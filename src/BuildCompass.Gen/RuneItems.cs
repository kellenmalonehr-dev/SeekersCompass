namespace BuildCompass.Gen;

/// `runes`: the consumables that only give runes when used (golden runes and their kin), read
/// from the game's data: goods whose use effect has a rune amount. The list is the source for
/// data/rune_items.txt, which the grace menu's rune line and the DLL both read.
static class RuneItems
{
    public record Item(int Goods, int Runes, string Name);

    public static List<Item> Find(GameData gd, Text text)
    {
        var effects = gd["SpEffectParam"];
        var found = new List<Item>();
        foreach (var g in gd["EquipParamGoods"].Rows)
        {
            if (g.Get<byte>("goodsType") != 0 || g.Get<short>("maxNum") < 2) continue;
            int soul = effects[g.Get<int>("refId_default")]?.Get<int>("soul") ?? 0;
            string name = text.Get("GoodsName", g.ID);
            if (soul <= 0 || name == null || name.StartsWith("[ERROR]")) continue;
            found.Add(new Item(g.ID, soul, name));
        }
        return found;
    }

    public static void Run()
    {
        var gd = GameData.Load(Program.In("regulation.bin"));
        var text = new Text(Program.In("msg", "engus", "item_dlc02.msgbnd.dcx"));
        foreach (var i in Find(gd, text)) Console.WriteLine($"{i.Goods} {i.Runes}   # {i.Name}");
    }
}
