using SoulsFormats;

namespace BuildCompass.Gen;

/// The grace menu's rune line: "use golden runes for the next level". It shows while the DLL says
/// the carried rune items can cover what is missing for the next level (flag Available).
///
/// A talk script can take items away but has no way to count runes, and the DLL can count but has
/// no way to take an item. So they share the work through event flags: choosing the line raises
/// Request; the DLL picks the items and raises one Step flag at a time, each meaning "take one of
/// item i"; the script takes it with the command the Twin Maiden Husks use for a handed-in bell
/// bearing (c1_52) and lowers the flag; when the DLL has added the runes it lowers Request, and
/// the script goes back to the grace menu.
static class RuneLine
{
    // Shared with the DLL (src/runes.rs). 76700-76799 is a gap in the game's grace flags; the map marks
    // would only reach it after 700 marks in the 76000s, and the hubs count down from 76999.
    public const uint Available = 76700, Request = 76701, StepBase = 76710;
    public const int MaxItems = 40;
    const int EntryText = 79_980_000;                     // 79_960_000+ is the hub list
    const int EntryIndex = 64;                            // 60-63 are the Compass, Loadouts, Camp and the hub list
    const int LevelUpText = 15000540, SortChestText = 15000395;

    /// The goods ids of data/rune_items.txt, in order.
    public static List<int> Items() =>
        File.ReadLines(Path.Combine(Program.Root, "data", "rune_items.txt")).Select(l => l.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length > 0).Select(w => int.Parse(w[0])).ToList();

    // The mod's own words (corrections welcome). No araae: see Loadouts/Texts.cs.
    static readonly Dictionary<string, string> Own = new()
    {
        ["engus"] = "Use Golden Runes for Next Level",
        ["frafr"] = "Utiliser des runes d'or pour le prochain niveau",
        ["deude"] = "Goldene Runen für die nächste Stufe verwenden",
        ["itait"] = "Usa rune dorate per il prossimo livello",
        ["spaes"] = "Usar runas doradas para el siguiente nivel",
        ["spaar"] = "Usar runas doradas para el siguiente nivel",
        ["porbr"] = "Usar runas douradas para o próximo nível",
        ["polpl"] = "Użyj złotych run na następny poziom",
        ["rusru"] = "Использовать золотые руны для следующего уровня",
        ["jpnjp"] = "黄金のルーンを使って次のレベルに届かせる",
        ["zhocn"] = "使用黄金卢恩补足下一级所需",
        ["zhotw"] = "使用黃金盧恩補足下一級所需",
        ["korkr"] = "황금 룬을 사용해 다음 레벨 채우기",
        ["thath"] = "ใช้รูนทองคำให้พอสำหรับเลเวลถัดไป",
    };

    public static Dictionary<int, string> Texts(string lang) => new() { [EntryText] = Own.GetValueOrDefault(lang, Own["engus"]) };

    /// Adds the line and its subroutine to the grace talk script inside the common talk file.
    public static void AddTo(BND4 commonTalk, HashSet<uint> usedFlags, GameData gd)
    {
        var items = Items();
        if (items.Count > MaxItems) throw new Exception($"data/rune_items.txt has {items.Count} items; the flags are laid out for {MaxItems}");
        var goods = gd["EquipParamGoods"];
        foreach (int id in items.Where(id => goods[id] == null)) Console.WriteLine($"  rune line: goods {id} is not in this game's data; its line stays unused");
        foreach (uint f in new[] { Available, Request }.Concat(Enumerable.Range(0, MaxItems).Select(i => StepBase + (uint)i)))
            if (usedFlags.Contains(f)) throw new Exception($"rune line: event flag {f} is already used");

        var file = commonTalk.Files.First(f => f.Name.EndsWith("t000001000.esd"));
        var esd = ESD.Read(file.Bytes);
        var (menuId, menu) = esd.StateGroups.Single(g => g.Value.Values.Any(s => AddsLine(s, LevelUpText)));
        var listState = menu.Values.Single(s => AddsLine(s, SortChestText));
        if (AddsLine(listState, EntryText)) throw new Exception("this talk script already has the rune line");
        var show = menu.Values.Single(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 10));
        var dispatch = menu[show.Conditions.Single().TargetState.Value].Conditions;
        byte[] returned = menu.Values.SelectMany(s => s.Conditions).First(c => c.Evaluator.Length >= 2 && c.Evaluator[0] == 0xB9 && c.Evaluator[1] == 0xBA).Evaluator;
        byte[] always = EzState.Lit(1);
        long callState = menu.Keys.Max() + 1;
        long ourGroup = esd.StateGroups.Keys.Where(k => k > 1_000_000_000).Min() - 1;

        // 1. The line, shown while the DLL says it would work.
        listState.EntryCommands.Add(new ESD.CommandCall(5, 19, FlagIs(Available, true), EzState.Lit(EntryIndex), EzState.Lit(EntryText), EzState.Lit(-1)));

        // 2. Choosing it calls the subroutine, then rebuilds the grace menu.
        bool usesRegister = dispatch[0].Evaluator.Contains((byte)0xA7);
        dispatch.Insert(dispatch.Count - 1, new ESD.Condition { TargetState = callState, Evaluator = usesRegister ? ResultIs(EntryIndex) : ResultCall(EntryIndex) });
        menu[callState] = new ESD.State
        {
            EntryCommands = { new ESD.CommandCall(6, (int)ourGroup) },
            Conditions = { new ESD.Condition { Evaluator = returned, Subconditions = { new ESD.Condition { TargetState = 1, Evaluator = always } } } },
        };

        // 3. The subroutine: ask, take what the DLL names one item at a time, leave when it is done.
        const long Ask = 0, Wait = 1, Exit = 9;
        var group = new Dictionary<long, ESD.State>
        {
            [Ask] = new ESD.State { EntryCommands = { SetFlag(Request, true) }, Conditions = { new ESD.Condition { TargetState = Wait, Evaluator = always } } },
        };
        var wait = new ESD.State();
        for (int i = 0; i < items.Count; i++)
        {
            long take = 100 + 2 * i, lower = take + 1;
            wait.Conditions.Add(new ESD.Condition { TargetState = take, Evaluator = FlagIsCond(StepBase + (uint)i, true) });
            // c1_52(3, goods, -1) and then c1_11(flag, off), in two states as the Husks' script has them.
            group[take] = new ESD.State
            {
                EntryCommands = { new ESD.CommandCall(1, 52, EzState.Lit(3), EzState.Lit(items[i]), EzState.Lit(-1)) },
                Conditions = { new ESD.Condition { TargetState = lower, Evaluator = always } },
            };
            group[lower] = new ESD.State
            {
                EntryCommands = { SetFlag(StepBase + (uint)i, false) },
                Conditions = { new ESD.Condition { TargetState = Wait, Evaluator = always } },
            };
        }
        // Done, or the DLL is not there to answer (the line's flag has gone off).
        wait.Conditions.Add(new ESD.Condition { TargetState = Exit, Evaluator = FlagIsCond(Request, false) });
        wait.Conditions.Add(new ESD.Condition { TargetState = Exit, Evaluator = FlagIsCond(Available, false), PassCommands = { SetFlag(Request, false) } });
        group[Wait] = wait;
        group[Exit] = new ESD.State
        {
            Conditions = { new ESD.Condition { Evaluator = always, PassCommands = { new ESD.CommandCall(7, -1, EzState.Lit(0)) } } }, // return 0
        };
        esd.StateGroups[ourGroup] = group;
        file.Bytes = esd.Write();
        Console.WriteLine($"rune line: grace menu group {menuId}, entry {EntryIndex} -> group {ourGroup} with {items.Count} rune items; flags {Available}, {Request}, {StepBase}-{StepBase + MaxItems - 1}");
    }

    // ---- expressions, in the forms vanilla t000001000 uses ----------------------

    const int GetEventFlag = 15, GetTalkListEntryResult = 23;

    static byte[] FlagIs(uint flag, bool on)
    {
        var b = EzState.Call(GetEventFlag, (int)flag).ToList();
        EzState.Int(b, on ? 1 : 0); b.Add(0x95);
        return EzState.End(b);
    }

    static byte[] FlagIsCond(uint flag, bool on)
    {
        var b = EzState.Call(GetEventFlag, (int)flag).ToList();
        b.Add(0xA6);
        EzState.Int(b, on ? 1 : 0); b.Add(0x95);
        return EzState.End(b);
    }

    static byte[] ResultCall(int index)
    {
        var b = EzState.Call(GetTalkListEntryResult).ToList();
        EzState.Int(b, index); b.Add(0x95);
        return EzState.End(b);
    }

    static byte[] ResultIs(int index)
    {
        var b = new List<byte> { 0xAF };
        EzState.Int(b, index); b.Add(0x95);
        return EzState.End(b);
    }

    static ESD.CommandCall SetFlag(uint flag, bool on) => new(1, 11, EzState.Lit((int)flag), EzState.Lit(on ? 1 : 0));

    static bool AddsLine(ESD.State s, int textId) => s.EntryCommands.Any(c =>
        (c.CommandBank is 1 or 5) && (c.CommandID is 19 or 149) && c.Arguments.Any(a => a.SequenceEqual(EzState.Lit(textId))));
}
