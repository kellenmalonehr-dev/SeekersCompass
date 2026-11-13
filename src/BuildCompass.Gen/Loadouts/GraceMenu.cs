using SoulsFormats;

namespace GraceLoadouts.Gen;

/// The "Loadouts" entry in the Site of Grace menu: Equip Loadout 1-5 (lit while the slot holds a
/// loadout), Save Gear to Loadout 1-5, and Leave. Picking a line only sets an event flag;
/// grace_loadouts.dll does the equipping or saving on the next frame (see Builder's flags).
///
/// Built by patching t000001000 (the grace talk script) the way Seeker's Compass does, with every
/// shape copied from that script: the grace menu is the state group that adds the Level Up line,
/// which clears the talk list (c1_20), adds lines (c1_19), shows it (c1_140 + c1_10) until the menu
/// closes, reads the choice (function 23) and jumps to a state that calls a subroutine group
/// (c6_group) and comes back to state 1. Our list is a new subroutine group in the same style. The
/// patch finds everything by content, so it also applies on top of Convergence or Seeker's Compass.
static class GraceMenu
{
    // EventTextForTalk ids (talk-menu lines). Seeker's Compass uses 79_900_000-79_900_199;
    // collisions with the game are checked when patching.
    const int TalkTextBase = 79_920_000;
    const int EntryText = TalkTextBase, LeaveText = TalkTextBase + 1;
    static int EquipText(int n) => TalkTextBase + 10 + n;
    static int SaveText(int n) => TalkTextBase + 20 + n;

    // Grace menu lines we recognize by text id.
    const int LevelUpText = 15000540, SortChestText = 15000395;
    // Talk-list index of our line: unused by vanilla, Convergence and Seeker's Compass (60).
    const int EntryIndex = 61;
    // Our own list: Equip n = n, Save n = 10 + n, Leave = 99.
    const int SaveIndex = 10, LeaveIndex = 99;

    public static Dictionary<int, string> Texts(Texts t)
    {
        var d = new Dictionary<int, string> { [EntryText] = t.Menu, [LeaveText] = t.Leave };
        for (int n = 1; n <= Builder.Slots; n++)
        {
            d[EquipText(n)] = t.Equip(n);
            d[SaveText(n)] = t.Save(n);
        }
        return d;
    }

    public static void Build(string mod)
    {
        string src = Program.In("script", "talk", "m00_00_00_00.talkesdbnd.dcx");
        var bnd = BND4.Read(src);
        var file = bnd.Files.First(f => f.Name.EndsWith("t000001000.esd"));
        var esd = ESD.Read(file.Bytes);

        // Find the grace menu by its lines rather than by number.
        var (menuId, menu) = esd.StateGroups.Single(g => g.Value.Values.Any(s => AddsLine(s, LevelUpText)));
        var listState = menu.Values.Single(s => AddsLine(s, SortChestText));      // our line goes after Sort Chest
        if (AddsLine(listState, EntryText)) throw new Exception("this talk script already has the Loadouts line");
        var show = menu.Values.Single(s => s.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 10)); // c1_10: show list
        byte[] menuClosed = show.Conditions.Single().Evaluator;
        var dispatch = menu[show.Conditions.Single().TargetState.Value].Conditions; // where the choice is read
        if (dispatch.Any(c => c.Evaluator.SequenceEqual(ResultIs(EntryIndex, true)) || c.Evaluator.SequenceEqual(ResultIs(EntryIndex, false)) || c.Evaluator.SequenceEqual(ResultCall(EntryIndex))))
            throw new Exception($"grace menu already uses index {EntryIndex}");
        // "Subroutine finished" condition, copied from any state that calls one.
        byte[] returned = menu.Values.SelectMany(s => s.Conditions).First(c => c.Evaluator.Length >= 2 && c.Evaluator[0] == 0xB9 && c.Evaluator[1] == 0xBA).Evaluator;
        byte[] always = EzState.Lit(1);
        long ourCallState = menu.Keys.Max() + 1;
        long ourGroup = esd.StateGroups.Keys.Where(k => k > 1_000_000_000).Min() - 1; // below the lowest subroutine
        Console.WriteLine($"grace menu group {menuId}: list state {menu.First(kv => kv.Value == listState).Key}, our state {ourCallState}, our group {ourGroup}");

        // 1. Our line in the grace menu, always shown.
        listState.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(EntryIndex), EzState.Lit(EntryText), EzState.Lit(-1)));

        // 2. Choosing it calls our group, then rebuilds the grace menu (state 1). The choice is read the
        // way this script already reads it: via r0 (vanilla) or by calling function 23 again (Convergence).
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

        // 3. The loadout list.
        var group = new Dictionary<long, ESD.State>();
        const long Start = 0, List = 1, Show = 2, Choose = 3, Exit = 9;
        group[Start] = Goto(always, List);

        var list = new ESD.State();
        list.EntryCommands.Add(new ESD.CommandCall(1, 20)); // clear list
        for (int n = 1; n <= Builder.Slots; n++)
        {
            // c1_149(index, text, -1, 0, glow): the line with the glowing indicator vanilla uses for
            // "Flasks" when an upgrade is available. Glow = the slot holds a loadout.
            uint stored = Builder.StoredFlag + (uint)(n - 1);
            list.EntryCommands.Add(new ESD.CommandCall(1, 149, EzState.Lit(n), EzState.Lit(EquipText(n)), EzState.Lit(-1), EzState.Lit(0), FlagIs(stored, true)));
        }
        for (int n = 1; n <= Builder.Slots; n++)
            list.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(SaveIndex + n), EzState.Lit(SaveText(n)), EzState.Lit(-1)));
        list.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(LeaveIndex), EzState.Lit(LeaveText), EzState.Lit(-1)));
        list.Conditions.Add(new ESD.Condition { TargetState = Show, Evaluator = always });
        group[List] = list;

        // Show it the way vanilla does (same commands and "menu closed" wait).
        group[Show] = new ESD.State
        {
            EntryCommands = show.EntryCommands.Select(Clone).ToList(),
            Conditions = { new ESD.Condition { TargetState = Choose, Evaluator = menuClosed } },
        };

        // Choosing a line sets its request flag (pass command) and shows the list again; the DLL
        // clears the flag once it has acted. Anything else (Leave, Back) exits.
        var choose = new ESD.State();
        bool first = true;
        for (int n = 1; n <= Builder.Slots; n++)
            foreach (var (index, flag) in new[] { (n, Builder.EquipFlag), (SaveIndex + n, Builder.SaveFlag) })
            {
                choose.Conditions.Add(new ESD.Condition
                {
                    TargetState = List,
                    Evaluator = ResultIs(index, first),
                    PassCommands = { SetFlag(flag + (uint)(n - 1), true) },
                });
                first = false;
            }
        choose.Conditions.Add(new ESD.Condition { TargetState = Exit, Evaluator = always });
        group[Choose] = choose;
        group[Exit] = new ESD.State
        {
            Conditions = { new ESD.Condition { Evaluator = always, PassCommands = { new ESD.CommandCall(7, -1, EzState.Lit(0)) } } }, // return 0
        };
        esd.StateGroups[ourGroup] = group;

        file.Bytes = esd.Write();
        string outDir = Path.Combine(mod, "script", "talk");
        Directory.CreateDirectory(outDir);
        bnd.Write(Path.Combine(outDir, "m00_00_00_00.talkesdbnd.dcx"));
        Console.WriteLine($"grace menu: entry {EntryIndex} -> group {ourGroup} with {Builder.Slots} loadouts");
    }

    // ---- expressions, in the exact forms vanilla t000001000 uses --------------

    const int GetEventFlag = 15, GetTalkListEntryResult = 23;

    /// `15 <flag> call1 <v> ==` (talk-list condition form, no 0xA6).
    static byte[] FlagIs(uint flag, bool on)
    {
        var b = EzState.Call(GetEventFlag, (int)flag).ToList();
        EzState.Int(b, on ? 1 : 0); b.Add(0x95);
        return EzState.End(b);
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
