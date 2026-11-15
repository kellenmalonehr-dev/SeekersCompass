using SoulsFormats;

namespace GraceItems.Gen;

/// The "Camp" entry in the Site of Grace menu: the character's owned Grace Items (equipped ones
/// glow) and Leave. Choosing an item only sets event flags; the DLL equips or unequips it, then
/// clears the request, and the list is rebuilt (see DESIGN.md).
///
/// Built the way Grace Loadouts patches t000001000, with the same shapes copied from that script.
/// Lines that depend on flags use c5_19 / c5_149 (add a line if a condition holds), as vanilla's own
/// grace menu does. The patch finds everything by content, so it also applies on top of Convergence,
/// Seeker's Compass or Grace Loadouts.
static class CampMenu
{
    // EventTextForTalk ids. Seeker's Compass uses 79_900_000-79_900_199 and Grace Loadouts
    // 79_920_000+; collisions with the game or the base layer are checked when patching.
    const int TalkTextBase = 79_940_000;
    const int EntryText = TalkTextBase, LeaveText = TalkTextBase + 1, EntryNewText = TalkTextBase + 2, BuffNoneText = TalkTextBase + 3;
    static int ItemText(int n) => TalkTextBase + 100 + n;
    static int MinutesText(int m) => TalkTextBase + 1000 + m;
    static int KillsText(int k) => TalkTextBase + 2000 + k;
    /// The pop-up for item n, or for the welcome index.
    static int FoundText(int n) => TalkTextBase + 3000 + n;

    // Grace menu lines we recognize by text id.
    const int LevelUpText = 15000540, SortChestText = 15000395;
    // Talk-list index of our line: unused by vanilla, Convergence, Seeker's Compass (60) and Grace
    // Loadouts (61).
    const int EntryIndex = 62;
    // Our own list: item n = n + 1, the two status lines 90 and 91, Leave = 99.
    const int LeaveIndex = 99, BuffIndex = 90, KillsIndex = 91;
    // Vanilla's OK dialog closing: `f58(0) == 0`, as its own dialog subroutines wait.
    static readonly byte[] DialogClosed = { 0x7A, 0x40, 0x85, 0xA6, 0x40, 0x95, 0xA1 };

    public static Dictionary<int, string> Texts(Texts t, List<Item> items)
    {
        var d = new Dictionary<int, string>
        {
            [EntryText] = t.Camp,
            [LeaveText] = t.Leave,
            [EntryNewText] = $"{t.Camp} ({t.NewItem})",
            [BuffNoneText] = t.BuffNone,
            [FoundText(Builder.WelcomeIndex)] = t.Welcome,
        };
        for (int n = 0; n < items.Count; n++)
        {
            d[ItemText(n)] = t.ItemLine(items[n]);
            d[FoundText(n)] = $"{t.Found}\n{t.ItemLine(items[n])}";
        }
        for (int m = 1; m <= Builder.MaxMinutes; m++) d[MinutesText(m)] = t.BuffMinutes(m);
        for (int k = 0; k <= Builder.MaxKills; k++) d[KillsText(k)] = t.Kills(k == Builder.MaxKills ? $"{k}+" : $"{k}");
        return d;
    }

    public static void Build(string mod, List<Item> items)
    {
        string src = Program.In("script", "talk", "m00_00_00_00.talkesdbnd.dcx");
        var bnd = BND4.Read(src);
        var file = bnd.Files.First(f => f.Name.EndsWith("t000001000.esd"));
        var esd = ESD.Read(file.Bytes);

        // Find the grace menu by its lines rather than by number.
        var (menuId, menu) = esd.StateGroups.Single(g => g.Value.Values.Any(s => AddsLine(s, LevelUpText)));
        var listState = menu.Values.Single(s => AddsLine(s, SortChestText));
        if (AddsLine(listState, EntryText)) throw new Exception("this talk script already has the Camp line");
        var show = menu.Values.Single(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 10)); // c1_10: show list
        byte[] menuClosed = show.Conditions.Single().Evaluator;
        var dispatch = menu[show.Conditions.Single().TargetState.Value].Conditions;
        if (dispatch.Any(c => c.Evaluator.SequenceEqual(ResultIs(EntryIndex, true)) || c.Evaluator.SequenceEqual(ResultIs(EntryIndex, false)) || c.Evaluator.SequenceEqual(ResultCall(EntryIndex))))
            throw new Exception($"grace menu already uses index {EntryIndex}");
        byte[] returned = menu.Values.SelectMany(s => s.Conditions).First(c => c.Evaluator.Length >= 2 && c.Evaluator[0] == 0xB9 && c.Evaluator[1] == 0xBA).Evaluator;
        byte[] always = EzState.Lit(1);
        long ourCallState = menu.Keys.Max() + 1;
        long ourGroup = esd.StateGroups.Keys.Where(k => k > 1_000_000_000).Min() - 1; // below the lowest subroutine
        Console.WriteLine($"grace menu group {menuId}: our state {ourCallState}, our group {ourGroup}");

        // 1. Our line in the grace menu, only while the DLL runs (it keeps the alive flag on), so the
        // list can never wait on a DLL that isn't there.
        // While a found item waits to be shown, the line glows and says so.
        listState.EntryCommands.Add(new ESD.CommandCall(5, 19, AllOf((Builder.AliveFlag, true), (Builder.FoundPendingFlag, false)), EzState.Lit(EntryIndex), EzState.Lit(EntryText), EzState.Lit(-1)));
        listState.EntryCommands.Add(new ESD.CommandCall(5, 149, AllOf((Builder.AliveFlag, true), (Builder.FoundPendingFlag, true)), EzState.Lit(EntryIndex), EzState.Lit(EntryNewText), EzState.Lit(-1), EzState.Lit(0), EzState.Lit(1)));

        // 2. Choosing it calls our group, then rebuilds the grace menu (state 1).
        bool usesRegister = dispatch[0].Evaluator.Contains((byte)0xA7);
        dispatch.Insert(dispatch.Count - 1, new ESD.Condition
        {
            TargetState = ourCallState,
            Evaluator = usesRegister ? ResultIs(EntryIndex, first: false) : ResultCall(EntryIndex),
        });
        menu[ourCallState] = new ESD.State
        {
            EntryCommands = { new ESD.CommandCall(6, (int)ourGroup) },
            Conditions = { new ESD.Condition { Evaluator = returned, Subconditions = { new ESD.Condition { TargetState = 1, Evaluator = always } } } },
        };

        // 3. The item list.
        var group = new Dictionary<long, ESD.State>();
        const long Start = 0, List = 1, Show = 2, Choose = 3, Wait = 4, Announce = 5, Acknowledged = 6, Exit = 9, Popup = 100;

        // Found items first: one pop-up each. After each, the menu raises the ack flag and waits for
        // the DLL to put up the next one (or clear the pending flag) and lower it.
        group[Start] = new ESD.State
        {
            Conditions =
            {
                new ESD.Condition { TargetState = Announce, Evaluator = FlagIs(Builder.FoundPendingFlag, true) },
                new ESD.Condition { TargetState = List, Evaluator = always },
            },
        };
        var announce = new ESD.State();
        var popups = Enumerable.Range(0, items.Count).Append(Builder.WelcomeIndex);
        foreach (int n in popups)
        {
            announce.Conditions.Add(new ESD.Condition { TargetState = Popup + n, Evaluator = NumberIs(Builder.FoundIndexFlag, n) });
            group[Popup + n] = new ESD.State
            {
                EntryCommands = { new ESD.CommandCall(1, 17, EzState.Lit(7), EzState.Lit(FoundText(n)), EzState.Lit(1), EzState.Lit(0), EzState.Lit(1)) },
                Conditions = { new ESD.Condition { TargetState = Acknowledged, Evaluator = DialogClosed, PassCommands = { SetFlag(Builder.FoundAckFlag, true) } } },
            };
        }
        // An index with no pop-up: tell the DLL it was seen, so the queue can't stall.
        announce.Conditions.Add(new ESD.Condition { TargetState = Acknowledged, Evaluator = always, PassCommands = { SetFlag(Builder.FoundAckFlag, true) } });
        group[Announce] = announce;
        group[Acknowledged] = new ESD.State { Conditions = { new ESD.Condition { TargetState = Start, Evaluator = FlagIs(Builder.FoundAckFlag, false) } } };

        var list = new ESD.State();
        list.EntryCommands.Add(new ESD.CommandCall(1, 20)); // clear list
        // Status: the running buff's minutes left, then the kills banked at this rest.
        list.EntryCommands.Add(new ESD.CommandCall(5, 19, NumberIs(Builder.MinutesFlag, 0), EzState.Lit(BuffIndex), EzState.Lit(BuffNoneText), EzState.Lit(-1)));
        for (int m = 1; m <= Builder.MaxMinutes; m++)
            list.EntryCommands.Add(new ESD.CommandCall(5, 19, NumberIs(Builder.MinutesFlag, m), EzState.Lit(BuffIndex), EzState.Lit(MinutesText(m)), EzState.Lit(-1)));
        for (int k = 0; k <= Builder.MaxKills; k++)
            list.EntryCommands.Add(new ESD.CommandCall(5, 19, NumberIs(Builder.KillsFlag, k), EzState.Lit(KillsIndex), EzState.Lit(KillsText(k)), EzState.Lit(-1)));
        for (int n = 0; n < items.Count; n++)
        {
            uint owned = Builder.OwnedFlag + (uint)n, equipped = Builder.EquippedFlag + (uint)n;
            // c5_149(condition, index, text, -1, 0, glow): owned items only, lit while equipped.
            foreach (bool on in new[] { true, false })
                list.EntryCommands.Add(new ESD.CommandCall(5, 149, Both(owned, equipped, on), EzState.Lit(n + 1), EzState.Lit(ItemText(n)), EzState.Lit(-1), EzState.Lit(0), EzState.Lit(on ? 1 : 0)));
        }
        list.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(LeaveIndex), EzState.Lit(LeaveText), EzState.Lit(-1)));
        list.Conditions.Add(new ESD.Condition { TargetState = Show, Evaluator = always });
        group[List] = list;

        group[Show] = new ESD.State
        {
            EntryCommands = show.EntryCommands.Select(Clone).ToList(),
            Conditions = { new ESD.Condition { TargetState = Choose, Evaluator = menuClosed } },
        };

        // Choosing item n writes n into the index bits and raises the request; the list comes back
        // once the DLL has acted and cleared the request. Anything else (Leave, Back) exits.
        var choose = new ESD.State();
        for (int n = 0; n < items.Count; n++)
        {
            var c = new ESD.Condition { TargetState = Wait, Evaluator = ResultIs(n + 1, first: n == 0) };
            for (int b = 0; b < Builder.IndexBits; b++)
                c.PassCommands.Add(SetFlag(Builder.IndexBitsFlag + (uint)b, ((n >> b) & 1) == 1));
            c.PassCommands.Add(SetFlag(Builder.RequestFlag, true));
            choose.Conditions.Add(c);
        }
        // The status lines only show information; choosing one keeps the list open.
        choose.Conditions.Add(new ESD.Condition { TargetState = List, Evaluator = ResultIs(BuffIndex, first: false) });
        choose.Conditions.Add(new ESD.Condition { TargetState = List, Evaluator = ResultIs(KillsIndex, first: false) });
        choose.Conditions.Add(new ESD.Condition { TargetState = Exit, Evaluator = always });
        group[Choose] = choose;
        group[Wait] = new ESD.State { Conditions = { new ESD.Condition { TargetState = List, Evaluator = FlagIs(Builder.RequestFlag, false) } } };
        group[Exit] = new ESD.State
        {
            Conditions = { new ESD.Condition { Evaluator = always, PassCommands = { new ESD.CommandCall(7, -1, EzState.Lit(0)) } } }, // return 0
        };
        esd.StateGroups[ourGroup] = group;

        file.Bytes = esd.Write();
        string outDir = Path.Combine(mod, "script", "talk");
        Directory.CreateDirectory(outDir);
        bnd.Write(Path.Combine(outDir, "m00_00_00_00.talkesdbnd.dcx"));
        Console.WriteLine($"grace menu: entry {EntryIndex} -> group {ourGroup} with {items.Count} items");
    }

    // ---- expressions, in the exact forms vanilla t000001000 uses --------------

    const int GetEventFlag = 15, GetTalkListEntryResult = 23;

    /// `15 <flag> call1 <v> ==` (talk-list condition form, no 0xA6).
    static byte[] FlagIs(uint flag, bool on)
    {
        var b = new List<byte>();
        AddFlagTest(b, flag, on);
        return EzState.End(b);
    }

    /// `15 <owned> call1 1 == cont 15 <equipped> call1 <v> == &&`, as vanilla's c5_149 lines combine two flags.
    static byte[] Both(uint owned, uint equipped, bool equippedOn)
    {
        var b = new List<byte>();
        AddFlagTest(b, owned, true);
        b.Add(0xA6);
        AddFlagTest(b, equipped, equippedOn);
        b.Add(0x98);
        return EzState.End(b);
    }

    /// Every flag test joined with `cont ... &&`, the chain Both() uses for two.
    static byte[] AllOf(params (uint flag, bool on)[] tests)
    {
        var b = new List<byte>();
        for (int i = 0; i < tests.Length; i++)
        {
            if (i > 0) b.Add(0xA6);
            AddFlagTest(b, tests[i].flag, tests[i].on);
            if (i > 0) b.Add(0x98);
        }
        return EzState.End(b);
    }

    /// The number held in binary in NumberBits flags from `start` equals `value`.
    static byte[] NumberIs(uint start, int value) =>
        AllOf(Enumerable.Range(0, Builder.NumberBits).Select(bit => (start + (uint)bit, ((value >> bit) & 1) == 1)).ToArray());

    static void AddFlagTest(List<byte> b, uint flag, bool on)
    {
        b.AddRange(EzState.Call(GetEventFlag, (int)flag));
        EzState.Int(b, on ? 1 : 0);
        b.Add(0x95);
    }

    /// `23 call0 <i> ==`: the chosen index read by calling function 23 directly (Convergence's form).
    static byte[] ResultCall(int index)
    {
        var b = EzState.Call(GetTalkListEntryResult).ToList();
        EzState.Int(b, index); b.Add(0x95);
        return EzState.End(b);
    }

    /// Whether a state adds a talk-list line with this text (c1_19 / c5_19 / c1_149 / c5_149).
    static bool AddsLine(ESD.State s, int textId) => s.EntryCommands.Any(c =>
        (c.CommandBank is 1 or 5) && (c.CommandID is 19 or 149) && c.Arguments.Any(a => a.SequenceEqual(EzState.Lit(textId))));

    /// The talk list's chosen index, read the way vanilla's dispatch state does: the first branch
    /// reads it once into r0 (`23 call0 set_r0 <i> ==`), later branches compare r0 (`get_r0 <i> ==`).
    static byte[] ResultIs(int index, bool first)
    {
        var b = first ? EzState.Call(GetTalkListEntryResult).Append((byte)0xA7).ToList() : new List<byte> { 0xAF };
        EzState.Int(b, index); b.Add(0x95);
        return EzState.End(b);
    }

    /// c1_11(flag, state): SetEventFlag, as vanilla state 6 uses it.
    static ESD.CommandCall SetFlag(uint flag, bool on) => new(1, 11, EzState.Lit((int)flag), EzState.Lit(on ? 1 : 0));

    static ESD.State Goto(byte[] always, long target) => new() { Conditions = { new ESD.Condition { TargetState = target, Evaluator = always } } };

    static ESD.CommandCall Clone(ESD.CommandCall c) => new(c.CommandBank, c.CommandID, c.Arguments.Select(a => a.ToArray()).ToArray());
}
