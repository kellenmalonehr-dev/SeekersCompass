using SoulsFormats;

namespace BuildCompass.Gen;

/// One game language's words for the mod's text. English keeps the mod's own wording; every other
/// language reuses the game's official translations (looked up by the English entry's id) and falls
/// back to English where the game has no equivalent.
class Loc
{
    public readonly string Lang;
    public bool IsEnglish => Lang == "engus";
    readonly Text items;
    readonly Dictionary<int, string> menuText = new(), talkText = new();

    Loc(string lang)
    {
        Lang = lang;
        items = new Text(Program.In("msg", lang, "item_dlc02.msgbnd.dcx"));
        foreach (var f in BND4.Read(Program.In("msg", lang, "menu_dlc02.msgbnd.dcx")).Files)
        {
            string n = Path.GetFileNameWithoutExtension(f.Name);
            var target = n.StartsWith("GR_MenuText") ? menuText : n.StartsWith("EventTextForTalk") ? talkText : null;
            if (target == null) continue;
            foreach (var e in FMG.Read(f.Bytes).Entries)
                if (!string.IsNullOrWhiteSpace(e.Text)) target.TryAdd(e.ID, e.Text);
        }
    }

    public static Loc For(string lang) => new Loc(lang);

    /// Languages unpacked in the game (or base layer): folders with both item and menu text.
    public static List<Loc> All()
    {
        var langs = new[] { Program.GameDir, Program.Base }.Where(r => r != null)
            .Select(r => Path.Combine(r, "msg")).Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories).Select(Path.GetFileName)
            .Where(l => File.Exists(Program.In("msg", l, "item_dlc02.msgbnd.dcx")) && File.Exists(Program.In("msg", l, "menu_dlc02.msgbnd.dcx")))
            .Distinct().OrderBy(l => l != "engus").ThenBy(l => l).ToList();
        return langs.Select(l => new Loc(l)).ToList();
    }

    string Menu(int id, string english) => IsEnglish ? english : menuText.GetValueOrDefault(id) ?? english;

    // ---- game words (GR_MenuText / EventTextForTalk ids found by matching English text) ----
    static readonly Dictionary<Stat, int> StatShortIds = new() { [Stat.STR] = 11300, [Stat.DEX] = 11301, [Stat.INT] = 11302, [Stat.FAI] = 11303, [Stat.ARC] = 11304 };
    static readonly Dictionary<Stat, int> StatLongIds = new() { [Stat.STR] = 10403, [Stat.DEX] = 10404, [Stat.INT] = 10406, [Stat.FAI] = 10407, [Stat.ARC] = 10409 };
    static readonly Dictionary<Kind, int> KindIds = new() { [Kind.Weapon] = 40550, [Kind.Spell] = 40510, [Kind.Armor] = 40551, [Kind.Talisman] = 40509 };
    static readonly Dictionary<string, int> SlotIds = new() { ["Head"] = 40505, ["Chest"] = 40506, ["Arms"] = 40507, ["Legs"] = 40508 };
    static readonly Dictionary<string, int> AttributeIds = new() { ["VIG"] = 10400, ["END"] = 10401, ["MND"] = 10402 };

    public string StatShort(Stat s) => Menu(StatShortIds[s], s.ToString());
    public string StatLong(Stat s) => Menu(StatLongIds[s], Builder.StatNames[s]);
    public string KindLabel(Kind k) => Menu(KindIds[k], Builder.KindNames[k]);
    public string Slot(string slot) => Menu(SlotIds[slot], slot);
    /// VIG/MND/END have no short form in the game's text; other languages get the full word.
    public string Attribute(string label) => AttributeIds.TryGetValue(label, out int id) ? Menu(id, label) : label;
    public string FP => Menu(10501, "FP");
    public string Build(IEnumerable<Stat> stats) => IsEnglish ? $"{string.Join("/", stats)} build" : string.Join("/", stats.Select(StatShort));
    /// "Leave", the word every grace and NPC menu uses to exit; English keeps "Done".
    public string Done => IsEnglish ? "Done" : talkText.GetValueOrDefault(15000720) ?? "Done";

    public string Name(Kind k, int id) => items.Name(k, id);
    public string TalismanInfo(int id) => items.TalismanInfo(id)?.Replace("\n", " ");

    // ---- the mod's own text (no game equivalent); translations by the author, corrections welcome ----
    static readonly Dictionary<string, string> CollectedWords = new()
    {
        ["engus"] = "(collected)", ["frafr"] = "(obtenu)", ["deude"] = "(erhalten)", ["itait"] = "(ottenuto)",
        ["spaes"] = "(obtenido)", ["spaar"] = "(obtenido)", ["porbr"] = "(obtido)", ["polpl"] = "(zdobyte)",
        ["rusru"] = "(получено)", ["jpnjp"] = "（入手済み）", ["zhocn"] = "（已获得）", ["zhotw"] = "（已獲得）",
        ["korkr"] = "(획득함)", ["thath"] = "(ได้รับแล้ว)",
        // No araae: the game stores Arabic as pre-joined presentation forms, which suggests its font
        // can't join plain Arabic letters, so Arabic falls back to English for the mod's own text.
    };
    /// The item's name, also used for its grace menu line.
    static readonly Dictionary<string, string> CompassNames = new()
    {
        ["engus"] = "Seeker's Compass", ["frafr"] = "Boussole du chercheur", ["deude"] = "Kompass des Suchenden",
        ["itait"] = "Bussola del cercatore", ["spaes"] = "Brújula del buscador", ["spaar"] = "Brújula del buscador",
        ["porbr"] = "Bússola do buscador", ["polpl"] = "Kompas poszukiwacza", ["rusru"] = "Компас искателя",
        ["jpnjp"] = "探求者の羅針盤", ["zhocn"] = "探求者罗盘", ["zhotw"] = "探求者羅盤",
        ["korkr"] = "탐구자의 나침반", ["thath"] = "เข็มทิศของผู้แสวงหา",
    };
    /// Written per language so the name takes that language's grammar (French "à la", Polish genitive
    /// "Kompasu", Russian dative "Компасу").
    static readonly Dictionary<string, string> CompassDescriptions = new()
    {
        ["engus"] = "Allows you to access the Seeker's Compass at any site of grace.",
        ["frafr"] = "Permet d'accéder à la Boussole du chercheur à n'importe quel site de grâce.",
        ["deude"] = "Ermöglicht an jedem Ort der Gnade den Zugriff auf den Kompass des Suchenden.",
        ["itait"] = "Permette di accedere alla Bussola del cercatore in qualsiasi luogo di grazia.",
        ["spaes"] = "Permite acceder a la Brújula del buscador en cualquier lugar de gracia.",
        ["spaar"] = "Permite acceder a la Brújula del buscador en cualquier sitio de gracia.",
        ["porbr"] = "Permite acessar a Bússola do buscador em qualquer local de graça.",
        ["polpl"] = "Umożliwia dostęp do Kompasu poszukiwacza w każdym miejscu łaski.",
        ["rusru"] = "Открывает доступ к Компасу искателя у любого места благодати.",
        ["jpnjp"] = "どの祝福でも探求者の羅針盤を利用できるようになる。",
        ["zhocn"] = "可在任意赐福处使用探求者罗盘。",
        ["zhotw"] = "可在任意賜福處使用探求者羅盤。",
        ["korkr"] = "모든 축복에서 탐구자의 나침반을 이용할 수 있다.",
        ["thath"] = "สามารถใช้เข็มทิศของผู้แสวงหาได้ที่จุดแห่งพรทุกแห่ง",
    };
    public string Collected => CollectedWords.GetValueOrDefault(Lang, CollectedWords["engus"]);
    public string CompassName => CompassNames.GetValueOrDefault(Lang, CompassNames["engus"]);
    public string CompassDescription => CompassDescriptions.GetValueOrDefault(Lang, CompassDescriptions["engus"]);

    // ---- fitting text into the map tooltip ----------------------------------------------------
    /// The tooltip shows one line and clips it at about 54 Latin characters' width; newlines hide
    /// the rest. CJK and Hangul glyphs are about twice as wide, so they count double.
    public const int MaxWidth = 50;

    public static int Width(string s) => s.Sum(c => c >= 0x1100 && (c <= 0x115F || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6)) ? 2 : 1);

    static string Cut(string s, int width)
    {
        int w = 0, i = 0;
        for (; i < s.Length; i++) { int cw = Width(s[i].ToString()); if (w + cw > width) break; w += cw; }
        return s.Substring(0, i);
    }

    /// "Name | details" using the most detailed variant that fits, then trims with "...".
    public string Fit(string name, string[] details, bool collected = false)
    {
        string suffix = collected ? " " + Collected : "";
        foreach (var d in details.Where(d => !string.IsNullOrEmpty(d)))
        {
            string s = $"{name} | {d}{suffix}";
            if (Width(s) <= MaxWidth) return s;
        }
        string last = details.LastOrDefault(d => !string.IsNullOrEmpty(d)) ?? "";
        string head = $"{name} | ";
        int room = MaxWidth - Width(head) - Width(suffix);
        if (room >= 8) return head + Cut(last, room - 3).TrimEnd(' ', ',') + "..." + suffix;
        return Width(name + suffix) <= MaxWidth ? name + suffix : Cut(name, MaxWidth - Width(suffix) - 3) + "..." + suffix;
    }
}
