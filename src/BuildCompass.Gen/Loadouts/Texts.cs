using SoulsFormats;

namespace GraceLoadouts.Gen;

/// The menu's words in one game language. "Leave" is the game's own; the rest has no game
/// equivalent, so it's the mod's own translation (corrections welcome).
class Texts
{
    public readonly string Lang;
    readonly Dictionary<int, string> talkText = new();

    public Texts(string lang)
    {
        Lang = lang;
        foreach (var f in BND4.Read(Program.In("msg", lang, "menu_dlc02.msgbnd.dcx")).Files)
            if (Path.GetFileNameWithoutExtension(f.Name).StartsWith("EventTextForTalk"))
                foreach (var e in FMG.Read(f.Bytes).Entries)
                    if (!string.IsNullOrWhiteSpace(e.Text)) talkText.TryAdd(e.ID, e.Text);
    }

    /// Languages unpacked in the game (or base layer).
    public static List<string> Languages() =>
        new[] { Program.GameDir, Program.Base }.Where(r => r != null)
            .Select(r => Path.Combine(r, "msg")).Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories).Select(Path.GetFileName)
            .Where(l => File.Exists(Program.In("msg", l, "menu_dlc02.msgbnd.dcx")))
            .Distinct().OrderBy(l => l != "engus").ThenBy(l => l).ToList();

    /// "Leave", the word every grace and NPC menu uses to exit.
    public string Leave => talkText.GetValueOrDefault(15000720) ?? "Leave";

    // No araae: the game stores Arabic as pre-joined presentation forms, which suggests its font
    // can't join plain Arabic letters, so Arabic falls back to English (as in Seeker's Compass).
    static readonly Dictionary<string, (string menu, string equip, string save)> Own = new()
    {
        ["engus"] = ("Loadouts", "Equip Loadout {0}", "Save Gear to Loadout {0}"),
        ["frafr"] = ("Ensembles", "Équiper l'ensemble {0}", "Enregistrer l'équipement dans l'ensemble {0}"),
        ["deude"] = ("Ausrüstungssets", "Set {0} anlegen", "Ausrüstung als Set {0} speichern"),
        ["itait"] = ("Set di equipaggiamento", "Indossa il set {0}", "Salva l'equipaggiamento nel set {0}"),
        ["spaes"] = ("Conjuntos", "Equipar conjunto {0}", "Guardar equipo en el conjunto {0}"),
        ["spaar"] = ("Conjuntos", "Equipar conjunto {0}", "Guardar equipo en el conjunto {0}"),
        ["porbr"] = ("Conjuntos", "Equipar conjunto {0}", "Salvar equipamento no conjunto {0}"),
        ["polpl"] = ("Zestawy", "Załóż zestaw {0}", "Zapisz ekwipunek jako zestaw {0}"),
        ["rusru"] = ("Комплекты", "Надеть комплект {0}", "Сохранить снаряжение в комплект {0}"),
        ["jpnjp"] = ("装備セット", "装備セット{0}を装備する", "現在の装備をセット{0}に登録する"),
        ["zhocn"] = ("装备组合", "装备组合{0}", "将当前装备存为组合{0}"),
        ["zhotw"] = ("裝備組合", "裝備組合{0}", "將目前裝備存為組合{0}"),
        ["korkr"] = ("장비 세트", "장비 세트 {0} 착용", "현재 장비를 세트 {0}에 저장"),
        ["thath"] = ("ชุดอุปกรณ์", "สวมชุดอุปกรณ์ {0}", "บันทึกอุปกรณ์ลงชุด {0}"),
    };

    (string menu, string equip, string save) Mine => Own.GetValueOrDefault(Lang, Own["engus"]);
    public string Menu => Mine.menu;
    public string Equip(int n) => string.Format(Mine.equip, n);
    public string Save(int n) => string.Format(Mine.save, n);
}
