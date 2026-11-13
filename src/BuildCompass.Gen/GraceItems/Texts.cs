using SoulsFormats;

namespace GraceItems.Gen;

/// The menu's words in one game language. "Leave" is the game's own; "Camp" is the mod's own
/// translation (corrections welcome). Item names are English everywhere for now.
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

    // Status and pop-up text: English everywhere for now, like the item names.
    public string NewItem => "new Grace Item";
    public string Found => "Grace Item found:";
    public string Welcome => "Grace Items from the sites of grace you've already discovered are waiting in Camp.";
    public string BuffNone => "Rest buff: none";
    public string BuffMinutes(int m) => m == 1 ? "Rest buff: 1 minute left" : $"Rest buff: {m} minutes left";
    public string Kills(string k) => $"Kills banked at this rest: {k}";

    /// "Leave", the word every grace and NPC menu uses to exit.
    public string Leave => talkText.GetValueOrDefault(15000720) ?? "Leave";

    static readonly Dictionary<string, string> CampWord = new()
    {
        ["engus"] = "Camp",
        ["frafr"] = "Campement",
        ["deude"] = "Lager",
        ["itait"] = "Accampamento",
        ["spaes"] = "Campamento",
        ["spaar"] = "Campamento",
        ["porbr"] = "Acampamento",
        ["polpl"] = "Obóz",
        ["rusru"] = "Лагерь",
        ["jpnjp"] = "野営",
        ["zhocn"] = "营地",
        ["zhotw"] = "營地",
        ["korkr"] = "야영지",
        ["thath"] = "ค่ายพัก",
    };

    // No araae: the game stores Arabic as pre-joined presentation forms, so Arabic falls back to
    // English, as in Seeker's Compass and Grace Loadouts.
    public string Camp => CampWord.GetValueOrDefault(Lang, CampWord["engus"]);
}
