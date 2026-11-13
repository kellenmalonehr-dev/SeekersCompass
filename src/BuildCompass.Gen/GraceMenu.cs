using SoulsFormats;

namespace BuildCompass.Gen;

/// The "Seeker's Compass" entry in the Site of Grace menu: a list of toggles, one per damage stat
/// and one "show all" per item kind. Selecting a line flips its flag and reopens the list, whose
/// lines are marked with the current state, until the player picks Done or backs out.
///
/// Built by patching vanilla t000001000 (the grace talk script). Every shape used here is copied
/// from that script: the grace menu is state group 2147483616, which clears the talk list (c1_20),
/// adds lines (c1_19 / conditional c5_19), shows it (c1_140 + c1_10) until the menu closes, reads the
/// choice (function 23 into r0) and jumps to a state that calls a subroutine group (c6_group) and
/// comes back to state 1. Our toggle list is a new subroutine group in the same style.
/// One map mark for the grace menu to recompute: its own flag, the flags that show it, and its pickup.
record Mark(uint Flag, uint[] Shows, uint Pickup, Kind Kind, Stat[] Stats);

static class GraceMenu
{
    // EventTextForTalk ids (talk-menu lines). Collisions with vanilla are checked when patching.
    public const int TalkTextBase = 79_900_000;
    public const int EntryText = TalkTextBase;      // "Seeker's Compass" in the grace menu
    public const int DoneText = TalkTextBase + 1;
    // Toggle line i's label; whether it's on is shown by the line's glow, not its text.
    static int ToggleText(int i) => TalkTextBase + 100 + i;

    // Grace menu lines we recognize by text id, so the patch works on vanilla and on overhauls
    // (Convergence renumbers subroutines and list indexes, and compiles conditions differently).
    const int LevelUpText = 15000540, SortChestText = 15000395;
    const int EntryIndex = 60;        // talk-list index of our line (unused by vanilla and Convergence)
    const int DoneIndex = 99;

    /// Toggle lines in menu order: the five stats, the kinds, then "Collected".
    public static List<string> ToggleLabels() =>
        Enum.GetValues<Stat>().Select(s => Builder.StatNames[s])
            .Concat(Enum.GetValues<Kind>().Select(k => Builder.KindNames[k])).Append("Collected").ToList();

    /// The menu's lines in one language: the game's own words for stats, item types and "Leave",
    /// and the compass's name in that language.
    public static Dictionary<int, string> Texts(Loc loc)
    {
        var t = new Dictionary<int, string> { [EntryText] = loc.CompassName, [DoneText] = loc.Done };
        var labels = Enum.GetValues<Stat>().Select(loc.StatLong).Concat(Enum.GetValues<Kind>().Select(loc.KindLabel)).Append(loc.CollectedLabel).ToList();
        for (int i = 0; i < labels.Count; i++) t[ToggleText(i)] = labels[i];
        return t;
    }

    public static void Build(string mod, Dictionary<Stat, uint> statFlag, Dictionary<Kind, uint> kindFlag, uint collectedFlag, Visibility vis, List<Mark> marks)
    {
        string src = Program.In("script", "talk", "m00_00_00_00.talkesdbnd.dcx");
        var bnd = BND4.Read(src);
        var file = bnd.Files.First(f => f.Name.EndsWith("t000001000.esd"));
        var esd = ESD.Read(file.Bytes);

        // Find the grace menu by its lines rather than by number.
        var (menuId, menu) = esd.StateGroups.Single(g => g.Value.Values.Any(s => AddsLine(s, LevelUpText)));
        var listState = menu.Values.Single(s => AddsLine(s, SortChestText));      // our line goes after Sort chest
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

        // 1. Our line in the grace menu, only while the compass is carried.
        listState.EntryCommands.Add(new ESD.CommandCall(5, 19,
            OwnsGoods(Builder.CompassGoodsId), EzState.Lit(EntryIndex), EzState.Lit(EntryText), EzState.Lit(-1)));

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

        // 3. The toggle list.
        var flags = Enum.GetValues<Stat>().Select(s => statFlag[s]).Concat(Enum.GetValues<Kind>().Select(k => kindFlag[k])).Append(collectedFlag).ToList();
        var group = new Dictionary<long, ESD.State>();
        const long Start = 0, Build = 1, Show = 2, Choose = 3, Exit = 9;
        group[Start] = Goto(always, Build);

        var build = new ESD.State();
        build.EntryCommands.Add(new ESD.CommandCall(1, 20)); // clear list
        for (int i = 0; i < flags.Count; i++)
        {
            // c1_149(index, text, -1, 0, glow): the list line with the glowing indicator vanilla uses for
            // "Flasks" when an upgrade is available (group 2147483576). Glow = the toggle's flag.
            build.EntryCommands.Add(new ESD.CommandCall(1, 149, EzState.Lit(i + 1), EzState.Lit(ToggleText(i)), EzState.Lit(-1), EzState.Lit(0), FlagIs(flags[i], true)));
        }
        build.EntryCommands.Add(new ESD.CommandCall(1, 19, EzState.Lit(DoneIndex), EzState.Lit(DoneText), EzState.Lit(-1)));
        build.Conditions.Add(new ESD.Condition { TargetState = Show, Evaluator = always });
        group[Build] = build;

        // Show it the way vanilla does (same commands and "menu closed" wait as state 3).
        group[Show] = new ESD.State
        {
            EntryCommands = show.EntryCommands.Select(Clone).ToList(),
            Conditions = { new ESD.Condition { TargetState = Choose, Evaluator = menuClosed } },
        };

        // Choosing line i flips flag i (pass command) and rebuilds; anything else exits.
        var choose = new ESD.State();
        for (int i = 0; i < flags.Count; i++)
        {
            long toggle = 10 + i;
            choose.Conditions.Add(new ESD.Condition { TargetState = toggle, Evaluator = ResultIs(i + 1, first: i == 0) });
            group[toggle] = new ESD.State
            {
                Conditions =
                {
                    new ESD.Condition { TargetState = Build, Evaluator = FlagIsCond(flags[i], true), PassCommands = { SetFlag(flags[i], false) } },
                    new ESD.Condition { TargetState = Build, Evaluator = always, PassCommands = { SetFlag(flags[i], true) } },
                },
            };
        }
        // Leaving (Done or Back) first recomputes the derived visibility flags, then returns.
        const long ComputeStart = 100;
        choose.Conditions.Add(new ESD.Condition { TargetState = ComputeStart, Evaluator = always });
        AddCompute(group, ComputeStart, Exit, always, statFlag, kindFlag, collectedFlag, vis, marks);
        group[Choose] = choose;
        group[Exit] = new ESD.State
        {
            Conditions = { new ESD.Condition { Evaluator = always, PassCommands = { new ESD.CommandCall(7, -1, EzState.Lit(0)) } } }, // return 0
        };
        esd.StateGroups[ourGroup] = group;

        file.Bytes = esd.Write();
        SelfTest(ESD.Read(file.Bytes).StateGroups[ourGroup], ComputeStart, Exit, statFlag, kindFlag, collectedFlag, marks);
        string outDir = Path.Combine(mod, "script", "talk");
        Directory.CreateDirectory(outDir);
        bnd.Write(Path.Combine(outDir, "m00_00_00_00.talkesdbnd.dcx"));
        Console.WriteLine($"grace menu: entry {EntryIndex} -> group {ourGroup} with {flags.Count} toggles");
    }

    /// Runs the written compute chain over many toggle and pickup combinations and compares every
    /// mark with the menu's rules, written out separately here. A mismatch fails the build.
    static void SelfTest(Dictionary<long, ESD.State> group, long start, long done,
        Dictionary<Stat, uint> statFlag, Dictionary<Kind, uint> kindFlag, uint collectedFlag, List<Mark> marks)
    {
        var rng = new Random(1);
        int steps = 0;
        const int Runs = 300;
        for (int run = 0; run < Runs; run++)
        {
            double chance = run == 0 ? 0 : run == 1 ? 1 : rng.NextDouble();
            var on = new HashSet<uint>();
            foreach (var f in statFlag.Values.Concat(kindFlag.Values).Append(collectedFlag))
                if (rng.NextDouble() < chance) on.Add(f);
            foreach (var m in marks.Where(m => m.Pickup != 0))
                if (rng.NextDouble() < chance) on.Add(m.Pickup);
            // Old mark values must not survive a recompute.
            foreach (var m in marks) if (rng.Next(2) == 0) on.Add(m.Flag);

            int taken = 0;
            for (long id = start; id != done; taken++)
            {
                var state = group[id];
                var c = state.Conditions.First(x => Passes(x.Evaluator, on));
                foreach (var cmd in c.PassCommands)
                {
                    uint flag = (uint)BitConverter.ToInt32(cmd.Arguments[0], 1);
                    if (cmd.Arguments[1][0] == 65) on.Add(flag); else on.Remove(flag);
                }
                id = c.TargetState.Value;
            }
            steps = Math.Max(steps, taken);

            var stats = statFlag.Where(s => on.Contains(s.Value)).Select(s => s.Key).ToList();
            var kinds = Catalog.StatKinds.Where(k => on.Contains(kindFlag[k])).ToList();
            foreach (var m in marks)
            {
                bool matches = !Catalog.StatKinds.Contains(m.Kind) ? on.Contains(kindFlag[m.Kind])
                    : stats.Count == 0 ? kinds.Contains(m.Kind)
                    : m.Stats.Intersect(stats).Any() && (kinds.Count == 0 || kinds.Contains(m.Kind));
                bool hidden = m.Pickup != 0 && on.Contains(m.Pickup) && !on.Contains(collectedFlag);
                if (on.Contains(m.Flag) != (matches && !hidden))
                    throw new Exception($"grace menu self-test: mark {m.Flag} ({m.Kind}) wrong in run {run}");
            }
        }
        Console.WriteLine($"grace menu self-test: {Runs} combinations of {marks.Count} marks OK, up to {steps} steps per menu close");
    }

    /// Reads back the two condition forms the chain uses: always, and FlagIsCond.
    static bool Passes(byte[] e, HashSet<uint> on)
    {
        if (e.Length == 2) return true; // always
        bool set = on.Contains((uint)BitConverter.ToInt32(e, 2));
        return set == (e[8] == 65);
    }

    /// Recomputes Visibility's flags as a chain of states starting at `first` and ending at `done`.
    /// Only single-flag checks with pass commands are used (the shape the toggles proved in-game);
    /// within a state the first passing condition wins, which gives the AND/OR logic.
    static void AddCompute(Dictionary<long, ESD.State> group, long first, long done, byte[] always,
        Dictionary<Stat, uint> statFlag, Dictionary<Kind, uint> kindFlag, uint collectedFlag, Visibility vis, List<Mark> marks)
    {
        var steps = new List<Func<long, long, ESD.State>>(); // (thisId, nextId) => state

        ESD.Condition When(uint flag, bool on, long target, params ESD.CommandCall[] pass) =>
            new() { TargetState = target, Evaluator = FlagIsCond(flag, on), PassCommands = pass.ToList() };
        ESD.Condition Else(long target, params ESD.CommandCall[] pass) =>
            new() { TargetState = target, Evaluator = always, PassCommands = pass.ToList() };

        // AnyStat / AnyKind: clear, then set if any member is on.
        void AnyOf(uint any, IEnumerable<uint> members)
        {
            steps.Add((id, next) => new ESD.State { Conditions = { Else(next, SetFlag(any, false)) } });
            foreach (var m in members)
                steps.Add((id, next) => new ESD.State { Conditions = { When(m, true, next, SetFlag(any, true)), Else(next) } });
        }
        AnyOf(vis.AnyStat, statFlag.Values);
        AnyOf(vis.AnyKind, Catalog.StatKinds.Select(k => kindFlag[k])); // bell bearings stand alone

        // StatInKind[k, s] = s && (k || !AnyKind): two states, the second reached only if s is on.
        foreach (var ((k, s), v) in vis.StatInKind)
        {
            steps.Add((id, next) => new ESD.State { Conditions = { When(statFlag[s], false, next + 1, SetFlag(v, false)), Else(next) } });
            steps.Add((id, next) => new ESD.State
            {
                Conditions = { When(kindFlag[k], true, next, SetFlag(v, true)), When(vis.AnyKind, false, next, SetFlag(v, true)), Else(next, SetFlag(v, false)) },
            });
        }

        // AllOfKind[k] = k && !AnyStat.
        foreach (var (k, a) in vis.AllOfKind)
            steps.Add((id, next) => new ESD.State
            {
                Conditions = { When(kindFlag[k], false, next, SetFlag(a, false)), When(vis.AnyStat, true, next, SetFlag(a, false)), Else(next, SetFlag(a, true)) },
            });

        // Then each mark's own flag, with "Collected" read once: on, a mark just follows its filters;
        // off, its pickup flag hides it first.
        long branch = first + steps.Count, follow = branch + 1, hide = follow + marks.Count;
        for (int i = 0; i < steps.Count; i++)
            group[first + i] = steps[i](first + i, first + i + 1);
        group[branch] = new ESD.State { Conditions = { When(collectedFlag, true, follow), Else(hide) } };
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            long toFollow = i + 1 < marks.Count ? follow + i + 1 : done, toHide = i + 1 < marks.Count ? hide + i + 1 : done;
            var shown = group[follow + i] = new ESD.State();
            foreach (var f in m.Shows) shown.Conditions.Add(When(f, true, toFollow, SetFlag(m.Flag, true)));
            shown.Conditions.Add(Else(toFollow, SetFlag(m.Flag, false)));
            var hidden = group[hide + i] = new ESD.State();
            if (m.Pickup != 0) hidden.Conditions.Add(When(m.Pickup, true, toHide, SetFlag(m.Flag, false)));
            foreach (var f in m.Shows) hidden.Conditions.Add(When(f, true, toHide, SetFlag(m.Flag, true)));
            hidden.Conditions.Add(Else(toHide, SetFlag(m.Flag, false)));
        }
    }

    // ---- expressions, in the exact forms vanilla t000001000 uses --------------

    const int GetEventFlag = 15, GetTalkListEntryResult = 23, ComparePlayerInventoryNumber = 47;

    /// `47 3 <id> 2 0 0 call5 1 ==`: carries more than 0 of goods <id> (talk-list condition form).
    static byte[] OwnsGoods(int goodsId)
    {
        var b = EzState.Call(ComparePlayerInventoryNumber, 3, goodsId, 2, 0, 0).ToList();
        EzState.Int(b, 1); b.Add(0x95);
        return EzState.End(b);
    }

    /// `15 <flag> call1 <v> ==` (talk-list condition form, no 0xA6).
    static byte[] FlagIs(uint flag, bool on)
    {
        var b = EzState.Call(GetEventFlag, (int)flag).ToList();
        EzState.Int(b, on ? 1 : 0); b.Add(0x95);
        return EzState.End(b);
    }

    /// `15 <flag> call1 cont 1 ==` (state-condition form: 0xA6 after the call).
    static byte[] FlagIsCond(uint flag, bool on)
    {
        var b = EzState.Call(GetEventFlag, (int)flag).ToList();
        b.Add(0xA6);
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
