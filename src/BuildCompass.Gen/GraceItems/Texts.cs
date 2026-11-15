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

    // ---- item names -----------------------------------------------------------------------
    // The mod's own translations (corrections welcome). Each row: the five seats, the five
    // keepsakes, what each keepsake's stacks give (in the keepsakes' order), then "stacks", "seat"
    // and "min". An item whose name isn't in the English row (someone edited grace_items.txt)
    // keeps the name it has.
    static readonly string[] English =
    {
        "Worn Bedroll", "Straw Bedding", "Traveler's Rug", "Fine Rug", "Lord's Carpet",
        "Crystal of Fury", "Votive Candle of Warding", "Platter of Leeching", "Waterskin of Second Wind", "Pack of the Bulwark",
        "damage", "shell", "healing", "stamina", "poise",
        "stacks", "seat", "min",
    };

    /// The word for what a keepsake's stacks give, by the effect named in grace_items.txt.
    static readonly Dictionary<string, string> EffectWord = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fury"] = "damage", ["ward"] = "shell", ["leech"] = "healing", ["secondwind"] = "stamina", ["bulwark"] = "poise",
    };

    static readonly Dictionary<string, string[]> ItemWords = new()
    {
        ["frafr"] = new[] { "Couchage usé", "Litière de paille", "Tapis de voyageur", "Beau tapis", "Tapis seigneurial",
            "Cristal de fureur", "Cierge votif de protection", "Plateau de drain", "Outre de second souffle", "Paquetage du rempart",
            "dégâts", "bouclier", "soin", "énergie", "équilibre", "cumuls", "assise", "min" },
        ["deude"] = new[] { "Abgenutzte Schlafrolle", "Strohlager", "Reiseteppich", "Feiner Teppich", "Fürstenteppich",
            "Kristall des Zorns", "Votivkerze des Schutzes", "Platte des Aderlasses", "Wasserschlauch des zweiten Atems", "Bündel des Bollwerks",
            "Schaden", "Schild", "Heilung", "Ausdauer", "Statur", "Stapel", "Sitz", "Min." },
        ["itait"] = new[] { "Giaciglio logoro", "Giaciglio di paglia", "Tappeto da viaggio", "Tappeto pregiato", "Tappeto del lord",
            "Cristallo della furia", "Candela votiva della protezione", "Vassoio del salasso", "Otre del secondo fiato", "Fagotto del baluardo",
            "danni", "scudo", "cura", "energia", "equilibrio", "cariche", "seduta", "min" },
        ["spaes"] = new[] { "Petate gastado", "Lecho de paja", "Alfombra de viajero", "Alfombra fina", "Alfombra señorial",
            "Cristal de furia", "Vela votiva de protección", "Bandeja de sanguijuela", "Odre de segundo aliento", "Fardo del baluarte",
            "daño", "escudo", "curación", "energía", "aplomo", "acumulaciones", "asiento", "min" },
        ["spaar"] = new[] { "Petate gastado", "Lecho de paja", "Alfombra de viajero", "Alfombra fina", "Alfombra señorial",
            "Cristal de furia", "Vela votiva de protección", "Bandeja de sanguijuela", "Odre de segundo aliento", "Fardo del baluarte",
            "daño", "escudo", "curación", "energía", "aplomo", "acumulaciones", "asiento", "min" },
        ["porbr"] = new[] { "Saco de dormir gasto", "Leito de palha", "Tapete de viajante", "Tapete fino", "Tapete senhorial",
            "Cristal de fúria", "Vela votiva de proteção", "Bandeja de sanguessuga", "Odre de segundo fôlego", "Fardo do baluarte",
            "dano", "escudo", "cura", "energia", "equilíbrio", "acúmulos", "assento", "min" },
        ["polpl"] = new[] { "Zużyte posłanie", "Posłanie ze słomy", "Dywanik podróżnika", "Piękny dywan", "Dywan możnowładcy",
            "Kryształ furii", "Świeca wotywna ochrony", "Półmisek pijawki", "Bukłak drugiego oddechu", "Tobołek bastionu",
            "obrażenia", "osłona", "leczenie", "wytrzymałość", "równowaga", "ładunki", "siedzisko", "min" },
        ["rusru"] = new[] { "Потёртая подстилка", "Соломенная постель", "Дорожный коврик", "Изящный ковёр", "Ковёр лорда",
            "Кристалл ярости", "Вотивная свеча защиты", "Блюдо пиявки", "Бурдюк второго дыхания", "Котомка бастиона",
            "урон", "щит", "лечение", "запас сил", "баланс", "заряды", "сиденье", "мин" },
        ["jpnjp"] = new[] { "使い古しの寝袋", "藁の寝床", "旅人の敷物", "上等な敷物", "王の絨毯",
            "憤怒の結晶", "守護の奉納蝋燭", "吸命の大皿", "息継ぎの水袋", "堅守の荷袋",
            "ダメージ", "障壁", "回復", "スタミナ", "強靭度", "スタック", "座", "分" },
        ["zhocn"] = new[] { "破旧的铺盖", "稻草铺", "旅人的毯子", "精致的毯子", "王者的地毯",
            "狂怒结晶", "守护祈愿烛", "汲命托盘", "回气水袋", "壁垒行囊",
            "伤害", "护盾", "恢复", "精力", "强韧度", "层", "座位", "分钟" },
        ["zhotw"] = new[] { "破舊的鋪蓋", "稻草鋪", "旅人的毯子", "精緻的毯子", "王者的地毯",
            "狂怒結晶", "守護祈願燭", "汲命托盤", "回氣水袋", "壁壘行囊",
            "傷害", "護盾", "恢復", "精力", "強韌度", "層", "座位", "分鐘" },
        ["korkr"] = new[] { "낡은 침낭", "짚 잠자리", "여행자의 깔개", "고급 깔개", "군주의 융단",
            "분노의 결정", "수호의 봉헌초", "흡혈의 쟁반", "재기의 물주머니", "보루의 봇짐",
            "피해", "보호막", "회복", "스태미나", "강인도", "중첩", "자리", "분" },
        ["thath"] = new[] { "ที่นอนม้วนเก่า", "ที่นอนฟาง", "พรมนักเดินทาง", "พรมชั้นดี", "พรมแห่งลอร์ด",
            "ผลึกแห่งความเดือดดาล", "เทียนบูชาแห่งการคุ้มกัน", "ถาดแห่งการสูบเลือด", "ถุงน้ำแห่งลมหายใจที่สอง", "ห่อสัมภาระแห่งปราการ",
            "ความเสียหาย", "เกราะ", "ฟื้นฟู", "ความอึด", "ความทรหด", "สแต็ก", "ที่นั่ง", "นาที" },
    };

    string Word(string english)
    {
        int i = Array.FindIndex(English, e => e.Equals(english, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && ItemWords.TryGetValue(Lang, out var words) ? words[i] : english;
    }

    /// An item's name: its words in this language, with its tier number kept.
    public string ItemName(string name)
    {
        int space = name.LastIndexOf(' ');
        bool numbered = space > 0 && int.TryParse(name[(space + 1)..], out _);
        return numbered ? $"{Word(name[..space])} {name[(space + 1)..]}" : Word(name);
    }

    /// The menu line, e.g. "Worn Bedroll (seat, 15 min)" or "Crystal of Fury 1 (damage, 3 stacks)".
    public string ItemLine(Item item) => item.Seat
        ? $"{ItemName(item.Name)} ({Word("seat")}, {item.Minutes:0.#} {Word("min")})"
        : $"{ItemName(item.Name)} ({Word(EffectWord.GetValueOrDefault(item.Effect, item.Effect))}, {item.Max} {Word("stacks")})";
}
