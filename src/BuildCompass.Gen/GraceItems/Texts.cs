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
    // The mod's own translations (corrections welcome). Each row: the five seats, the nine
    // keepsakes, the nine things a keepsake raises, then "seat" and "min". An item whose name
    // isn't in the English row (someone edited grace_items.txt) keeps the name it has.
    static readonly string[] English =
    {
        "Worn Bedroll", "Straw Bedding", "Traveler's Rug", "Fine Rug", "Lord's Carpet",
        "Crystal of Strength", "Orrery of Dexterity", "Tome of Intelligence", "Votive Candle of Faith", "Platter of Arcane",
        "Jar of Vigor", "Lamp of Mind", "Pack of Endurance", "Waterskin of Stamina",
        "Strength", "Dexterity", "Intelligence", "Faith", "Arcane", "Vigor", "Mind", "Endurance", "stamina",
        "seat", "min",
    };

    static readonly Dictionary<string, string[]> ItemWords = new()
    {
        ["frafr"] = new[] { "Couchage usé", "Litière de paille", "Tapis de voyageur", "Beau tapis", "Tapis seigneurial",
            "Cristal de force", "Planétaire de dextérité", "Tome d'intelligence", "Cierge votif de foi", "Plateau d'ésotérisme",
            "Jarre de vigueur", "Lampe d'esprit", "Paquetage d'endurance", "Outre d'énergie",
            "Force", "Dextérité", "Intelligence", "Foi", "Ésotérisme", "Vigueur", "Esprit", "Endurance", "énergie", "assise", "min" },
        ["deude"] = new[] { "Abgenutzte Schlafrolle", "Strohlager", "Reiseteppich", "Feiner Teppich", "Fürstenteppich",
            "Kristall der Stärke", "Planetarium des Geschicks", "Foliant der Weisheit", "Votivkerze des Glaubens", "Platte der Arkanenergie",
            "Krug der Kraft", "Lampe des Geistes", "Bündel der Kondition", "Wasserschlauch der Ausdauer",
            "Stärke", "Geschick", "Weisheit", "Glaube", "Arkanenergie", "Kraft", "Geist", "Kondition", "Ausdauer", "Sitz", "Min." },
        ["itait"] = new[] { "Giaciglio logoro", "Giaciglio di paglia", "Tappeto da viaggio", "Tappeto pregiato", "Tappeto del lord",
            "Cristallo della forza", "Planetario della destrezza", "Tomo dell'intelligenza", "Candela votiva della fede", "Vassoio dell'arcano",
            "Giara del vigore", "Lampada della mente", "Fagotto della tempra", "Otre dell'energia",
            "Forza", "Destrezza", "Intelligenza", "Fede", "Arcano", "Vigore", "Mente", "Tempra", "energia", "seduta", "min" },
        ["spaes"] = new[] { "Petate gastado", "Lecho de paja", "Alfombra de viajero", "Alfombra fina", "Alfombra señorial",
            "Cristal de fuerza", "Planetario de destreza", "Tomo de inteligencia", "Vela votiva de fe", "Bandeja de arcano",
            "Vasija de vigor", "Lámpara de mente", "Fardo de aguante", "Odre de energía",
            "Fuerza", "Destreza", "Inteligencia", "Fe", "Arcano", "Vigor", "Mente", "Aguante", "energía", "asiento", "min" },
        ["spaar"] = new[] { "Petate gastado", "Lecho de paja", "Alfombra de viajero", "Alfombra fina", "Alfombra señorial",
            "Cristal de fuerza", "Planetario de destreza", "Tomo de inteligencia", "Vela votiva de fe", "Bandeja de arcano",
            "Vasija de vigor", "Lámpara de mente", "Fardo de resistencia", "Odre de energía",
            "Fuerza", "Destreza", "Inteligencia", "Fe", "Arcano", "Vigor", "Mente", "Resistencia", "energía", "asiento", "min" },
        ["porbr"] = new[] { "Saco de dormir gasto", "Leito de palha", "Tapete de viajante", "Tapete fino", "Tapete senhorial",
            "Cristal de força", "Planetário de destreza", "Tomo de inteligência", "Vela votiva de fé", "Bandeja de arcano",
            "Jarro de vigor", "Lâmpada de mente", "Fardo de fortitude", "Odre de energia",
            "Força", "Destreza", "Inteligência", "Fé", "Arcano", "Vigor", "Mente", "Fortitude", "energia", "assento", "min" },
        ["polpl"] = new[] { "Zużyte posłanie", "Posłanie ze słomy", "Dywanik podróżnika", "Piękny dywan", "Dywan możnowładcy",
            "Kryształ siły", "Planetarium zręczności", "Księga inteligencji", "Świeca wotywna wiary", "Półmisek wiedzy tajemnej",
            "Dzban wigoru", "Lampa umysłu", "Tobołek kondycji", "Bukłak wytrzymałości",
            "Siła", "Zręczność", "Inteligencja", "Wiara", "Wiedza tajemna", "Wigor", "Umysł", "Kondycja", "wytrzymałość", "siedzisko", "min" },
        ["rusru"] = new[] { "Потёртая подстилка", "Соломенная постель", "Дорожный коврик", "Изящный ковёр", "Ковёр лорда",
            "Кристалл силы", "Планетарий ловкости", "Фолиант интеллекта", "Вотивная свеча веры", "Блюдо колдовства",
            "Кувшин жизненной силы", "Лампа ума", "Котомка выносливости", "Бурдюк запаса сил",
            "Сила", "Ловкость", "Интеллект", "Вера", "Колдовство", "Жизненная сила", "Ум", "Выносливость", "запас сил", "сиденье", "мин" },
        ["jpnjp"] = new[] { "使い古しの寝袋", "藁の寝床", "旅人の敷物", "上等な敷物", "王の絨毯",
            "筋力の結晶", "技量の天球儀", "知力の書", "信仰の奉納蝋燭", "神秘の大皿",
            "生命力の壺", "精神力のランプ", "持久力の荷袋", "スタミナの水袋",
            "筋力", "技量", "知力", "信仰", "神秘", "生命力", "精神力", "持久力", "スタミナ", "座", "分" },
        ["zhocn"] = new[] { "破旧的铺盖", "稻草铺", "旅人的毯子", "精致的毯子", "王者的地毯",
            "力气结晶", "灵巧天球仪", "智力典籍", "信仰祈愿烛", "感应托盘",
            "生命力壶", "集中力灯", "耐力行囊", "精力水袋",
            "力气", "灵巧", "智力", "信仰", "感应", "生命力", "集中力", "耐力", "精力", "座位", "分钟" },
        ["zhotw"] = new[] { "破舊的鋪蓋", "稻草鋪", "旅人的毯子", "精緻的毯子", "王者的地毯",
            "力氣結晶", "靈巧天球儀", "智力典籍", "信仰祈願燭", "感應托盤",
            "生命力壺", "集中力燈", "耐力行囊", "精力水袋",
            "力氣", "靈巧", "智力", "信仰", "感應", "生命力", "集中力", "耐力", "精力", "座位", "分鐘" },
        ["korkr"] = new[] { "낡은 침낭", "짚 잠자리", "여행자의 깔개", "고급 깔개", "군주의 융단",
            "근력의 결정", "기량의 천구의", "지력의 서", "신앙의 봉헌초", "신비의 쟁반",
            "생명력의 항아리", "정신력의 등불", "지구력의 봇짐", "스태미나의 물주머니",
            "근력", "기량", "지력", "신앙", "신비", "생명력", "정신력", "지구력", "스태미나", "자리", "분" },
        ["thath"] = new[] { "ที่นอนม้วนเก่า", "ที่นอนฟาง", "พรมนักเดินทาง", "พรมชั้นดี", "พรมแห่งลอร์ด",
            "ผลึกแห่งพละกำลัง", "ลูกโลกดาราศาสตร์แห่งความคล่องแคล่ว", "ตำราแห่งสติปัญญา", "เทียนบูชาแห่งศรัทธา", "ถาดแห่งความลึกลับ",
            "ไหแห่งพลังชีวิต", "ตะเกียงแห่งจิตใจ", "ห่อสัมภาระแห่งความทนทาน", "ถุงน้ำแห่งความอึด",
            "พละกำลัง", "ความคล่องแคล่ว", "สติปัญญา", "ศรัทธา", "ความลึกลับ", "พลังชีวิต", "จิตใจ", "ความทนทาน", "ความอึด", "ที่นั่ง", "นาที" },
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

    /// The menu line, e.g. "Worn Bedroll (seat, 15 min)" or "Crystal of Strength 1 (+1 Strength)".
    public string ItemLine(Item item) => item.Seat
        ? $"{ItemName(item.Name)} ({Word("seat")}, {item.Minutes:0.#} {Word("min")})"
        : item.Stat.Equals("stamina", StringComparison.OrdinalIgnoreCase) ? $"{ItemName(item.Name)} (+{item.Max}% {Word("stamina")})" : $"{ItemName(item.Name)} (+{item.Max} {Word(item.Stat)})";
}
