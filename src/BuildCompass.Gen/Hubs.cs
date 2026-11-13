using System.Globalization;
using System.Numerics;
using SoulsFormats;

namespace BuildCompass.Gen;

/// `hubs [--base <folder>] [--out <folder>]`: writes the files of the Roundtable Hubs mod (its own
/// package, separate from Seeker's Compass):
/// - Roundtable Hold with the Twin Maiden Husks moved beside Smithing Master Hewg;
/// - boss rooms that serve as hubs once their boss is dead (data/hubs.txt): Roundtable's service
///   characters stand in them, and each is listed under Roundtable Hold in the grace list.
/// With --base (e.g. an installed Seeker's Compass), regulation.bin is built on top of that mod's.
static class Hubs
{
    const string Roundtable = "m11_10_00_00";
    const int RoundtableWarpRow = 111000; // the Table of Lost Grace in BonfireWarpParam
    const string TalkDummyModel = "c1000"; // invisible characters that carry a talk script; every grace has one

    /// A Roundtable character with the props that make up its place. FloorY is the floor under it
    /// in Roundtable Hold, so the group keeps its height above the floor wherever it goes.
    /// Props are named, or (PropModels) every prop of those models within PropRadius.
    record Group(string Key, string Character, float FloorY, string[] Props = null, string[] PropModels = null, float PropRadius = 0,
        (string prop, int dummyPoly, int sfx)? Sfx = null);

    // The Husks are an invisible talker; their body (AEG221_527) and the book piles around it
    // (AEG221_638) are separate props with no entity ids.
    static readonly Group Husks = new("husks", "c1000_9005", -22.609f, PropModels: new[] { "AEG221_527", "AEG221_638" }, PropRadius: 2.5f);
    // Hewg with his anvil (Roundtable's unburnt one), the props beside it, and the anchor of the
    // forge effect that Roundtable's event 11103700 attaches.
    static readonly Group Hewg = new("hewg", "c3451_9000", -22.61f,
        Props: new[] { "AEG221_610_4500", "AEG227_008_4502", "AEG099_303_9000", "AEG099_304_9000", "AEG099_090_9001" },
        Sfx: ("AEG099_090_9001", 100, 803450));

    static readonly Group[] Residents = { Hewg, Husks };

    /// A boss room used as a hub (a line of data/hubs.txt). Nth is its number among the hubs of
    /// its map, which keeps the ids it adds apart from the others'.
    record Hub(string Map, uint GraceEntity, uint BossDeadFlag, string SpotFile, bool Icon, bool LandingSide, int Nth)
    {
        // Entities and events are ids nothing in vanilla uses (checked as they're taken).
        uint Base => GraceEntity / 10000 * 10000;
        public uint FirstCharacterEntity => (uint)(Base + 790 + 10 * Nth);
        public uint FirstPropEntity => (uint)(Base + 1790 + 30 * Nth);
        public long FirstEventId => Base + 2790 + 2 * Nth;
    }

    /// Where a hub's travel entry is drawn, in metres from Roundtable Hold's own (east, up, north).
    /// Seen in game: an entry 150 m east of Roundtable's drew about 2060 m east and 770 m north of
    /// Roundtable Hold's emblem, which is not drawn where its entry is. This aims just east of it.
    static readonly Vector3 IconOffset = new(-1366, 0, -768);
    const int WarpRowBase = 99_000_000; // a hub's travel row is this plus the room's own row id

    static List<Hub> ReadHubs()
    {
        var hubs = new List<Hub>();
        foreach (string raw in File.ReadLines(DataFile("hubs.txt")))
        {
            var w = raw.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (w.Length < 3) continue;
            hubs.Add(new Hub(w[0], uint.Parse(w[1]), uint.Parse(w[2]), w.Skip(3).FirstOrDefault(x => x.StartsWith("spots="))?[6..],
                w.Skip(3).Contains("icon"), w.Skip(3).Contains("landing-side"), hubs.Count(h => h.Map == w[0])));
        }
        return hubs;
    }

    public static void Run(string[] args)
    {
        int bi = Array.IndexOf(args, "--base"), oi = Array.IndexOf(args, "--out");
        if (bi >= 0) Program.Base = args[bi + 1];
        string outDir = oi >= 0 ? Path.GetFullPath(Path.Combine(Program.Root, args[oi + 1])) : Path.Combine(Program.Root, "mod-hubs");
        Console.WriteLine($"base: {Program.Base ?? "vanilla"} -> {outDir}");
        foreach (var sub in new[] { "map", "script", "event" }) // leftovers of an earlier build
            if (Directory.Exists(Path.Combine(outDir, sub))) Directory.Delete(Path.Combine(outDir, sub), true);

        string holdPath = Program.In("map", "mapstudio", Roundtable + ".msb.dcx");
        var hold = MSBE.Read(holdPath);
        MoveHusks(hold, Spot.Read(DataFile("husks-spot.txt"))["husks"]);
        hold.Write(OutFile(outDir, "map", "mapstudio", Roundtable + ".msb.dcx"));

        var source = MSBE.Read(holdPath); // untouched, to copy characters from
        var holdTalk = BND4.Read(Program.In("script", "talk", Roundtable + ".talkesdbnd.dcx"));
        var emedf = new Emedf(Path.Combine(Program.Root, "deps", "DarkScript3", "DarkScript3", "Resources", "er-common.emedf.json"));
        var gd = GameData.Load(Program.In("regulation.bin"));
        var warp = gd["BonfireWarpParam"];

        var hubs = ReadHubs();
        int order = 0, skipped = 0;
        foreach (var room in hubs.GroupBy(h => h.Map))
        {
            string map = room.Key;
            string talkPath = Program.In("script", "talk", map + ".talkesdbnd.dcx");
            if (!File.Exists(talkPath))
            {
                Console.WriteLine($"{map}: SKIPPED, its talk file isn't unpacked ({talkPath})");
                skipped += room.Count();
                continue;
            }
            var msb = MSBE.Read(Program.In("map", "mapstudio", map + ".msb.dcx"));
            var talk = BND4.Read(talkPath);
            var emevd = EMEVD.Read(Program.In("event", map + ".emevd.dcx"));
            var addedTalk = new HashSet<string>();
            foreach (var hub in room) BuildHub(hub, order++, msb, talk, addedTalk, emevd, source, holdTalk, emedf, warp);
            msb.Write(OutFile(outDir, "map", "mapstudio", map + ".msb.dcx"));
            talk.Write(OutFile(outDir, "script", "talk", map + ".talkesdbnd.dcx"));
            emevd.Write(OutFile(outDir, "event", map + ".emevd.dcx"));
        }

        warp.Rows = warp.Rows.OrderBy(r => r.ID).ToList();
        gd.Regulation.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "BonfireWarpParam").Bytes = warp.Write();
        SoulsFormats.Cryptography.RegulationDecryptor.EncryptERRegulation(Path.Combine(outDir, "regulation.bin"), gd.Regulation);
        Console.WriteLine($"wrote {outDir}: {order} hubs in {hubs.Select(h => h.Map).Distinct().Count()} maps{(skipped > 0 ? $", {skipped} skipped" : "")}");
    }

    static string DataFile(string name) => Path.Combine(Program.Root, "data", name);

    static string OutFile(string outDir, params string[] parts)
    {
        string path = Path.Combine(new[] { outDir }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }

    static (MSBE.Part.Enemy character, List<MSBE.Part.Asset> props) Find(MSBE msb, Group g)
    {
        var character = msb.Parts.Enemies.FirstOrDefault(e => e.Name == g.Character) ?? throw new Exception($"{g.Character} not in {Roundtable}");
        var props = g.Props != null
            ? g.Props.Select(n => msb.Parts.Assets.FirstOrDefault(a => a.Name == n) ?? throw new Exception($"{n} not in {Roundtable}")).ToList()
            : msb.Parts.Assets.Where(a => g.PropModels.Contains(a.ModelName) && Vector3.Distance(a.Position, character.Position) < g.PropRadius).ToList();
        if (g.PropModels != null && !props.Any(a => a.ModelName == g.PropModels[0])) throw new Exception($"{g.Key}: no {g.PropModels[0]} beside {g.Character}");
        return (character, props);
    }

    /// Puts a character and its props on a spot of floor, turned as one group about the character.
    /// A facing of 0 looks along -Z and grows clockwise seen from above.
    static void Place(Group g, MSBE.Part.Enemy character, List<MSBE.Part.Asset> props, Spot spot)
    {
        var from = character.Position;
        var to = spot.Position + new Vector3(0, from.Y - g.FloorY, 0);
        float degrees = spot.Facing - character.Rotation.Y, turn = degrees * MathF.PI / 180;
        foreach (var a in props)
        {
            if (a.Rotation.X != 0 || a.Rotation.Z != 0) throw new Exception($"{a.Name} is tilted; only upright props can be turned");
            var d = a.Position - from;
            a.Position = to + new Vector3(d.X * MathF.Cos(turn) + d.Z * MathF.Sin(turn), d.Y, -d.X * MathF.Sin(turn) + d.Z * MathF.Cos(turn));
            a.Rotation = new Vector3(0, MathF.IEEERemainder(a.Rotation.Y + degrees, 360), 0);
        }
        character.Position = to;
        character.Rotation = new Vector3(0, spot.Facing, 0);
    }

    static void MoveHusks(MSBE msb, Spot spot)
    {
        var (talker, props) = Find(msb, Husks);
        // A prop is drawn only from the rooms its groups name, so the moved ones take the groups
        // (and floor) of a fixed prop already standing where they're going.
        var moved = props.ToHashSet();
        var local = msb.Parts.Assets
            .Where(a => !moved.Contains(a) && a.EntityID == 0 && !string.IsNullOrEmpty(a.UnkPartNames[3]) && a.Unk1.DrawGroups.Any(g => g != 0))
            .OrderBy(a => Vector3.Distance(a.Position, spot.Position)).First();
        Console.WriteLine($"husks: {talker.Position} facing {talker.Rotation.Y:0.#} -> floor {spot.Position} facing {spot.Facing:0.#}; {props.Count} props; room of {local.Name} ({local.UnkPartNames[3]}, {Vector3.Distance(local.Position, spot.Position):0.0} m away)");
        Place(Husks, talker, props, spot);
        foreach (var a in props)
        {
            a.Unk1 = local.Unk1.DeepCopy();
            a.UnkPartNames = (string[])local.UnkPartNames.Clone();
        }
        talker.CollisionPartName = local.UnkPartNames[3];
    }

    /// Spots for a room that has no spot file: the residents stand in an arc on the far side of
    /// the grace from where travellers land, 5 m from the grace and facing it, at the grace's
    /// height. Boss rooms are mostly flat around their grace; a room where this puts someone in
    /// a wall or off the floor gets a spot file instead.
    /// With landingSide (rooms where the far side is a slope, a doorway or rough ground) they stand
    /// on the travellers' side instead, at the landing spot's height and spread wide enough to leave
    /// that spot clear.
    static Dictionary<string, Spot> AutoSpots(MSBE msb, MSBE.Part.Asset grace, Group[] groups, bool landingSide)
    {
        float Distance = 5, Spread = landingSide ? 130 : 70;
        var landing = msb.Parts.Players.FirstOrDefault(p => p.EntityID == grace.EntityID - 970);
        var away = landing != null ? new Vector2(grace.Position.X - landing.Position.X, grace.Position.Z - landing.Position.Z) : Vector2.Zero;
        away = away.Length() > 0.5f ? Vector2.Normalize(away) : new Vector2(1, 0);
        if (landingSide) away = -away;
        float y = landingSide && landing != null ? landing.Position.Y : grace.Position.Y;
        Console.WriteLine($"  grace {grace.Position}, landing {landing?.Position.ToString() ?? "none"}");
        var spots = new Dictionary<string, Spot>();
        for (int i = 0; i < groups.Length; i++)
        {
            float a = (groups.Length == 1 ? 0 : -Spread / 2 + Spread * i / (groups.Length - 1)) * MathF.PI / 180;
            var d = new Vector2(away.X * MathF.Cos(a) - away.Y * MathF.Sin(a), away.X * MathF.Sin(a) + away.Y * MathF.Cos(a));
            // A part with facing f looks along (-sin f, -cos f); here it looks back at the grace, along -d.
            spots[groups[i].Key] = new Spot(new Vector3(grace.Position.X + d.X * Distance, y, grace.Position.Z + d.Y * Distance), MathF.Atan2(d.X, d.Y) * 180 / MathF.PI);
        }
        return spots;
    }

    static void BuildHub(Hub hub, int order, MSBE msb, BND4 talk, HashSet<string> addedTalk, EMEVD emevd, MSBE source, BND4 holdTalk, Emedf emedf, PARAM warp)
    {
        var grace = msb.Parts.Assets.FirstOrDefault(a => a.EntityID == hub.GraceEntity) ?? throw new Exception($"{hub.Map}: no grace with entity {hub.GraceEntity}");
        var spots = hub.SpotFile != null ? Spot.Read(DataFile(hub.SpotFile)) : AutoSpots(msb, grace, Residents, hub.LandingSide);
        var taken = msb.Parts.GetEntries().Select(p => p.EntityID).Where(id => id != 0).ToHashSet();
        var names = msb.Parts.GetEntries().Select(p => p.Name).ToHashSet();
        int nextName = 9790;
        uint nextCharacter = hub.FirstCharacterEntity, nextProp = hub.FirstPropEntity;
        string NewName(string model) { string n; do n = $"{model}_{nextName++}"; while (!names.Add(n)); return n; }
        int Instance(string name) => int.Parse(name[(name.LastIndexOf('_') + 1)..]);
        uint Entity(ref uint next) { while (!taken.Add(next)) next++; return next++; }

        string talkFolder = talk.Files[0].Name[..(talk.Files[0].Name.LastIndexOf('\\') + 1)];
        var characters = new List<uint>();
        var hidden = new List<uint>(); // prop entities
        var effects = new List<(uint prop, int dummyPoly, int sfx)>();

        foreach (var g in Residents)
        {
            // --- map: copies of the character and its props
            var spot = spots[g.Key];
            var (srcCharacter, srcProps) = Find(source, g);
            var character = (MSBE.Part.Enemy)srcCharacter.DeepCopy();
            var props = srcProps.Select(a => (MSBE.Part.Asset)a.DeepCopy()).ToList();
            Place(g, character, props, spot);

            // Lighting, draw groups and floor come from what already stands in the room: the nearest
            // fixed prop (at this height if there is one) and the nearest talk dummy.
            var fixedProps = msb.Parts.Assets.Where(a => a.EntityID == 0).ToList();
            var level = fixedProps.Where(a => MathF.Abs(a.Position.Y - spot.Position.Y) < 2).ToList();
            var localProp = (level.Count > 0 ? level : fixedProps).OrderBy(a => Vector3.Distance(a.Position, spot.Position)).First();
            var onFloor = msb.Parts.Enemies.Where(e => !string.IsNullOrEmpty(e.CollisionPartName)).ToList();
            // A talk dummy close by if there is one (every grace has one); else whoever stands nearest, often the boss.
            var dummies = onFloor.Where(e => e.ModelName == TalkDummyModel && Vector3.Distance(e.Position, spot.Position) < 15).ToList();
            var localCharacter = (dummies.Count > 0 ? dummies : onFloor).OrderBy(e => Vector3.Distance(e.Position, spot.Position)).First();
            Console.WriteLine($"{hub.Map} {hub.GraceEntity} {g.Key}: {(hub.SpotFile != null ? "spot file" : "auto")}, floor {spot.Position} facing {spot.Facing:0.#}, {props.Count} props; like prop {localProp.Name} ({Vector3.Distance(localProp.Position, spot.Position):0.0} m) and {localCharacter.Name} on {localCharacter.CollisionPartName} ({Vector3.Distance(localCharacter.Position, spot.Position):0.0} m); prop floor \"{localProp.UnkPartNames[3]}\"");

            character.Name = NewName(character.ModelName);
            character.InstanceID = Instance(character.Name);
            character.EntityID = Entity(ref nextCharacter);
            character.CollisionPartName = localCharacter.CollisionPartName;
            character.Unk1 = localCharacter.Unk1.DeepCopy();
            character.Gparam = localCharacter.Gparam.DeepCopy();
            character.Unk8 = localCharacter.Unk8.DeepCopy();
            character.TileLoad = localCharacter.TileLoad.DeepCopy();
            msb.Parts.Enemies.Add(character);
            characters.Add(character.EntityID);
            if (!msb.Models.Enemies.Any(m => m.Name == character.ModelName))
                msb.Models.Enemies.Add(new MSBE.Model.Enemy { Name = character.ModelName, SibPath = source.Models.Enemies.First(m => m.Name == character.ModelName).SibPath });

            for (int i = 0; i < props.Count; i++)
            {
                var a = props[i];
                a.Name = NewName(a.ModelName);
                a.InstanceID = Instance(a.Name);
                a.EntityID = Entity(ref nextProp); // so the event script can hide it
                a.SibPath = "";
                a.Unk1 = localProp.Unk1.DeepCopy();
                a.Unk2 = localProp.Unk2.DeepCopy();
                a.Gparam = localProp.Gparam.DeepCopy();
                a.Grass = localProp.Grass.DeepCopy();
                a.Unk8 = localProp.Unk8.DeepCopy();
                a.Unk9 = localProp.Unk9.DeepCopy();
                a.TileLoad = localProp.TileLoad.DeepCopy();
                a.Unk11 = localProp.Unk11.DeepCopy();
                a.UnkPartNames = (string[])localProp.UnkPartNames.Clone();
                a.UnkT54PartName = localProp.UnkT54PartName;
                msb.Parts.Assets.Add(a);
                hidden.Add(a.EntityID);
                if (g.Sfx is var (prop, dummyPoly, sfx) && srcProps[i].Name == prop) effects.Add((a.EntityID, dummyPoly, sfx));
                if (!msb.Models.Assets.Any(m => m.Name == a.ModelName))
                    msb.Models.Assets.Add(new MSBE.Model.Asset { Name = a.ModelName, SibPath = source.Models.Assets.First(m => m.Name == a.ModelName).SibPath });
            }

            // --- talk: the character's script, added once to this map's own scripts
            string talkName = $"t{character.TalkID}.esd";
            var esd = holdTalk.Files.First(f => f.Name.EndsWith(talkName));
            if (addedTalk.Add(talkName))
            {
                if (talk.Files.Any(f => f.Name.EndsWith(talkName))) throw new Exception($"{hub.Map} already has {talkName}");
                talk.Files.Add(new BinderFile(esd.Flags, talk.Files.Max(f => f.ID) + 1, talkFolder + talkName, esd.Bytes));
            }
        }

        // --- events: nobody can be hurt, nobody is there until the boss is dead, and effects run after
        long hideId = hub.FirstEventId, effectId = hub.FirstEventId + 1;
        if (emevd.Events.Any(e => e.ID == hideId || e.ID == effectId)) throw new Exception($"event {hideId} or {effectId} already exists in {hub.Map}");
        // End If Event Flag: end, state, event flag.
        EMEVD.Instruction EndIfBoss(bool dead) =>
            new(1003, 2, new byte[] { 0, (byte)(dead ? 1 : 0), 0, 0 }.Concat(BitConverter.GetBytes(hub.BossDeadFlag)).ToArray());
        var hide = new EMEVD.Event(hideId, EMEVD.Event.RestBehaviorType.Default);
        foreach (uint c in characters) hide.Instructions.Add(emedf.Make(2004, 15, c, 1)); // Set Character Invincibility: on
        hide.Instructions.Add(EndIfBoss(dead: true));
        foreach (uint c in characters) hide.Instructions.Add(emedf.Make(2004, 5, c, 0)); // Change Character Enable State: off
        foreach (uint a in hidden) hide.Instructions.Add(emedf.Make(2005, 3, a, 0)); // Change Asset Enable State: off
        var effect = new EMEVD.Event(effectId, EMEVD.Event.RestBehaviorType.Default);
        effect.Instructions.Add(EndIfBoss(dead: false));
        foreach (var (prop, dummyPoly, sfx) in effects) effect.Instructions.Add(emedf.Make(2006, 4, prop, dummyPoly, sfx)); // Create Asset-following SFX
        emevd.Events.Add(hide);
        emevd.Events.Add(effect);
        var init = emevd.Events.First(e => e.ID == 0).Instructions;
        init.Add(emedf.Make(2000, 0, 0, (uint)hideId, 0)); // Initialize Event
        init.Add(emedf.Make(2000, 0, 0, (uint)effectId, 0));

        // --- travel: a second entry for the room's grace, listed under Roundtable Hold. Where an
        // entry is drawn comes from its own position; where it leads comes from its grace. It
        // shows once the room's own grace has been found. Only "icon" rooms are drawn on the map:
        // the others ask for a zoom step the map never reaches, so they are in the list alone.
        var holdRow = warp[RoundtableWarpRow] ?? throw new Exception("Roundtable Hold's grace row is missing");
        var roomRow = warp.Rows.FirstOrDefault(r => r.ID < WarpRowBase && r.Get<uint>("bonfireEntityId") == hub.GraceEntity) ?? throw new Exception($"no grace row for entity {hub.GraceEntity}");
        int rowId = WarpRowBase + roomRow.ID;
        if (warp[rowId] != null) throw new Exception($"BonfireWarpParam row {rowId} already exists");
        var row = new PARAM.Row(roomRow) { ID = rowId, Name = "" };
        foreach (string f in new[] { "areaNo", "gridXNo", "gridZNo", "bonfireSubCategoryId" }) row[f].Value = holdRow[f].Value;
        row.Set("posX", holdRow.Get<float>("posX") + IconOffset.X);
        row.Set("posY", holdRow.Get<float>("posY") + IconOffset.Y);
        row.Set("posZ", holdRow.Get<float>("posZ") + IconOffset.Z);
        row.Set("bonfireSubCategorySortId", (ushort)(holdRow.Get<ushort>("bonfireSubCategorySortId") + 10 + order));
        if (!hub.Icon)
        {
            row.Set("dispMinZoomStep", (byte)255);
            row.Set("selectMinZoomStep", (byte)255);
        }
        warp.Rows.Add(row);
        Console.WriteLine($"{hub.Map} {hub.GraceEntity}: {characters.Count} characters, {hidden.Count} props, events {hideId} and {effectId}, travel row {rowId} to grace {roomRow.ID}{(hub.Icon ? " with a map icon" : "")}, residents shown by flag {hub.BossDeadFlag}");
    }

    /// A place on the floor and a facing in degrees. A spot file has lines of "name x y z yaw" with
    /// yaw in radians: the numbers are the layout of the position.txt that the DLL keeps writing
    /// for the player, so a spot can be taken by standing on it in game. Lines starting with # are
    /// notes. "name right-of other metres" puts a spot beside another one, to the right as seen by
    /// someone facing it, at the same height and facing.
    record Spot(Vector3 Position, float Facing)
    {
        public static Dictionary<string, Spot> Read(string path)
        {
            var spots = new Dictionary<string, Spot>();
            foreach (string line in File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
            {
                var w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(w[i], CultureInfo.InvariantCulture);
                if (w[1] == "right-of")
                {
                    var other = spots[w[2]];
                    float t = other.Facing * MathF.PI / 180;
                    spots[w[0]] = other with { Position = other.Position + new Vector3(MathF.Cos(t), 0, -MathF.Sin(t)) * F(3) };
                }
                else spots[w[0]] = new Spot(new Vector3(F(1), F(2), F(3)), F(4) * 180 / MathF.PI);
            }
            return spots;
        }
    }
}
