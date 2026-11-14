using SoulsFormats;

namespace BuildCompass.Gen;

/// The grace menu's hub list: every hub room whose grace the player has found, by its place name.
/// Choosing one makes it the default hub: its flag goes on and every other room's goes off, and
/// that room alone is drawn on the map beside Roundtable Hold (Hubs gives each room a second
/// travel entry that only exists while its flag is on). The chosen room's line glows.
///
/// Built by patching t000001000 the way the Loadouts and Camp lines are (Loadouts/GraceMenu.cs):
/// one more line in the grace menu, and a subroutine group for the list.
static class HubPicker
{
    /// A room in the list: its place in the list, the flag that says its grace is found, the flag
    /// that makes it the default hub, and its name in PlaceName.
    public record Room(int Order, uint Found, uint Chosen, int PlaceText);

    // EventTextForTalk ids. Seeker's Compass uses 79_900_000+, Loadouts 79_920_000+, Camp 79_940_000+.
    const int TalkTextBase = 79_960_000;
    const int EntryText = TalkTextBase;
    static int RoomText(Room r) => TalkTextBase + 100 + r.Order;
    const int LeaveText = 15000720;                       // the game's own "Leave"
    const int LevelUpText = 15000540, SortChestText = 15000395;
    // Talk-list index of the line in the grace menu: 60-62 are the Compass, Loadouts and Camp.
    const int EntryIndex = 63, LeaveIndex = 99;
    static int RoomIndex(Room r) => r.Order + 1;

    /// The line texts in one language: the grace menu line is Roundtable Hold's own name, and each
    /// room's line is its grace's name, so nothing here needs translating.
    public static Dictionary<int, string> Texts(Text place, int holdText, IEnumerable<Room> rooms)
    {
        var d = new Dictionary<int, string> { [EntryText] = place.Get("PlaceName", holdText) ?? "Roundtable Hold" };
        if (place.Get("PlaceName", holdText) == null) Console.WriteLine("  hub list: no place name for Roundtable Hold in this language; English used");
        foreach (var r in rooms) d[RoomText(r)] = place.Get("PlaceName", r.PlaceText) ?? $"Hub {r.Order + 1}";
        return d;
    }

    /// Adds the line and the list to the grace talk script inside the common talk file.
    public static void AddTo(BND4 commonTalk, List<Room> rooms)
    {
        var file = commonTalk.Files.First(f => f.Name.EndsWith("t000001000.esd"));
        var esd = ESD.Read(file.Bytes);

        var (menuId, menu) = esd.StateGroups.Single(g => g.Value.Values.Any(s => AddsLine(s, LevelUpText)));
        var listState = menu.Values.Single(s => AddsLine(s, SortChestText));
        if (AddsLine(listState, EntryText)) throw new Exception("this talk script already has the hub list line");
        var show = menu.Values.Single(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 10)); // c1_10: show list
        byte[] menuClosed = show.Conditions.Single().Evaluator;
        var dispatch = menu[show.Conditions.Single().TargetState.Value].Conditions;
        byte[] returned = menu.Values.SelectMany(s => s.Conditions).First(c => c.Evaluator.Length >= 2 && c.Evaluator[0] == 0xB9 && c.Evaluator[1] == 0xBA).Evaluator;
        byte[] always = EzState.Lit(1);
        long callState = menu.Keys.Max() + 1;
        long ourGroup = esd.StateGroups.Keys.Where(k => k > 1_000_000_000).Min() - 1;

        // 1. The line in the grace menu.
        listState.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(EntryIndex), EzState.Lit(EntryText), EzState.Lit(-1)));

        // 2. Choosing it calls the list, then rebuilds the grace menu. The choice is read the way this
        // script already reads it: through r0 (vanilla) or by calling function 23 again (Convergence).
        bool usesRegister = dispatch[0].Evaluator.Contains((byte)0xA7);
        dispatch.Insert(dispatch.Count - 1, new ESD.Condition
        {
            TargetState = callState,
            Evaluator = usesRegister ? ResultIs(EntryIndex, first: false) : ResultCall(EntryIndex),
        });
        menu[callState] = new ESD.State
        {
            EntryCommands = { new ESD.CommandCall(6, (int)ourGroup) },
            Conditions = { new ESD.Condition { Evaluator = returned, Subconditions = { new ESD.Condition { TargetState = 1, Evaluator = always } } } },
        };

        // 3. The list: found rooms only, the default one lit.
        const long Start = 0, List = 1, Show = 2, Choose = 3, Exit = 9;
        var group = new Dictionary<long, ESD.State> { [Start] = Goto(always, List) };
        var list = new ESD.State();
        list.EntryCommands.Add(new ESD.CommandCall(1, 20)); // clear list
        foreach (var r in rooms)
            foreach (bool lit in new[] { true, false })
                // c5_149(condition, index, text, -1, 0, glow)
                list.EntryCommands.Add(new ESD.CommandCall(5, 149, Both(r.Found, r.Chosen, lit), EzState.Lit(RoomIndex(r)), EzState.Lit(RoomText(r)), EzState.Lit(-1), EzState.Lit(0), EzState.Lit(lit ? 1 : 0)));
        list.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(LeaveIndex), EzState.Lit(LeaveText), EzState.Lit(-1)));
        list.Conditions.Add(new ESD.Condition { TargetState = Show, Evaluator = always });
        group[List] = list;
        group[Show] = new ESD.State
        {
            EntryCommands = show.EntryCommands.Select(c => new ESD.CommandCall(c.CommandBank, c.CommandID, c.Arguments.Select(a => a.ToArray()).ToArray())).ToList(),
            Conditions = { new ESD.Condition { TargetState = Choose, Evaluator = menuClosed } },
        };
        // Choosing a room turns its flag on and all the others' off, then shows the list again.
        var choose = new ESD.State();
        foreach (var r in rooms)
        {
            var c = new ESD.Condition { TargetState = List, Evaluator = ResultIs(RoomIndex(r), first: r == rooms[0]) };
            foreach (var other in rooms) c.PassCommands.Add(new ESD.CommandCall(1, 11, EzState.Lit((int)other.Chosen), EzState.Lit(other == r ? 1 : 0))); // SetEventFlag
            choose.Conditions.Add(c);
        }
        choose.Conditions.Add(new ESD.Condition { TargetState = Exit, Evaluator = always });
        group[Choose] = choose;
        group[Exit] = new ESD.State
        {
            Conditions = { new ESD.Condition { Evaluator = always, PassCommands = { new ESD.CommandCall(7, -1, EzState.Lit(0)) } } }, // return 0
        };
        esd.StateGroups[ourGroup] = group;
        file.Bytes = esd.Write();
        Console.WriteLine($"hub list: grace menu group {menuId}, entry {EntryIndex} -> group {ourGroup} with {rooms.Count} rooms");
    }

    // ---- expressions, in the forms vanilla t000001000 uses ----------------------

    const int GetEventFlag = 15, GetTalkListEntryResult = 23;

    static List<byte> Flag(uint flag, bool on)
    {
        var b = EzState.Call(GetEventFlag, (int)flag).ToList();
        EzState.Int(b, on ? 1 : 0); b.Add(0x95);
        return b;
    }

    /// `15 a call1 1 == cont 15 b call1 v == &&`
    static byte[] Both(uint a, uint b, bool bOn)
    {
        var e = Flag(a, true); e.Add(0xA6);
        e.AddRange(Flag(b, bOn)); e.Add(0x98);
        return EzState.End(e);
    }

    static byte[] ResultCall(int index)
    {
        var b = EzState.Call(GetTalkListEntryResult).ToList();
        EzState.Int(b, index); b.Add(0x95);
        return EzState.End(b);
    }

    static byte[] ResultIs(int index, bool first)
    {
        var b = first ? EzState.Call(GetTalkListEntryResult).Append((byte)0xA7).ToList() : new List<byte> { 0xAF };
        EzState.Int(b, index); b.Add(0x95);
        return EzState.End(b);
    }

    static bool AddsLine(ESD.State s, int textId) => s.EntryCommands.Any(c =>
        (c.CommandBank is 1 or 5) && (c.CommandID is 19 or 149) && c.Arguments.Any(a => a.SequenceEqual(EzState.Lit(textId))));

    static ESD.State Goto(byte[] always, long target) => new() { Conditions = { new ESD.Condition { TargetState = target, Evaluator = always } } };
}
