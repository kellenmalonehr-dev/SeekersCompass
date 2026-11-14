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

    // Roderika as the spirit tuner, with the stand beside her; she and Enia stand on their rooms' floors.
    static readonly Group Roderika = new("roderika", "c0000_9011", -21.56f, Props: new[] { "AEG221_498_4500" });
    // Finger Reader Enia on her seat.
    static readonly Group Enia = new("enia", "c2170_9000", -20.10f, Props: new[] { "AEG221_605_4500" });

    /// In placing order: the first two stand nearest the middle of the arc, the next two outside them.
    static readonly Group[] Residents = { Hewg, Husks, Roderika, Enia };

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
    /// Roundtable Hold's emblem, which is not drawn where its entry is. (-1366, -768) put it just east
    /// of the emblem; the user then asked for it closer in and a little lower, at a spot they marked
    /// with the map cursor: about 122 m further west and 84 m further south (screenshot of 2026-11-14,
    /// where the 544 m between emblem and icon measured 129 px).
    static readonly Vector3 IconOffset = new(-1488, 0, -852);
    const int WarpRowBase = 99_000_000; // a hub's travel row is this plus the room's own row id
    const int RoundtablePlaceName = 11100; // "Roundtable Hold" in PlaceName, the area's name
    const int IconRowBase = 98_000_000; // and its map entry, shown only while it is the default hub
    /// One flag per room, on for the default hub: free flags taken downward from here. The map
    /// marks take theirs upward from 78000, 77000 and 76000, so the two meet only if either grows a lot.
    const uint DefaultFlagTop = 76999;
    static Queue<uint> freeFlags = new();
    static readonly List<HubPicker.Room> rooms = new();
    static HashSet<uint> usedFlags = new();

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
        // --add-to <mod folder>: as a part of the main build, on top of what it has written there.
        int bi = Array.IndexOf(args, "--base"), oi = Array.IndexOf(args, "--out"), ai = Array.IndexOf(args, "--add-to");
        if (bi >= 0) Program.Base = args[bi + 1];
        string outDir = ai >= 0 ? Path.GetFullPath(args[ai + 1]) : oi >= 0 ? Path.GetFullPath(Path.Combine(Program.Root, args[oi + 1])) : Path.Combine(Program.Root, "mod-hubs");
        Console.WriteLine($"hubs: base {Program.Base ?? "vanilla"} -> {outDir}");
        // Leftovers of an earlier build. Map files are read from the game again every time: a map
        // that already has its residents must not get them twice.
        var hubMaps = ReadHubs().Select(h => h.Map).Append(Roundtable).ToHashSet();
        if (Directory.Exists(Path.Combine(outDir, "map"))) Directory.Delete(Path.Combine(outDir, "map"), true);
        // (As a part of the main build the talk files stay: the bell bearing step has just written them.)
        foreach (var sub in ai >= 0 ? new[] { "event" } : new[] { Path.Combine("script", "talk"), "event" })
            if (Directory.Exists(Path.Combine(outDir, sub)))
                foreach (var f in Directory.GetFiles(Path.Combine(outDir, sub)).Where(f => hubMaps.Contains(Path.GetFileName(f).Split('.')[0])))
                    File.Delete(f);
        if (ai >= 0) Program.Over = outDir;

        var gd = GameData.Load(Program.In("regulation.bin"));
        PrepareMerchant(gd);

        string holdPath = Program.In("map", "mapstudio", Roundtable + ".msb.dcx");
        var hold = MSBE.Read(holdPath);
        MoveHusks(hold, Spot.Read(DataFile("husks-spot.txt"))["husks"]);
        hold.Write(OutFile(outDir, "map", "mapstudio", Roundtable + ".msb.dcx"));

        var source = MSBE.Read(holdPath); // untouched, to copy characters from
        var holdTalk = BND4.Read(Program.In("script", "talk", Roundtable + ".talkesdbnd.dcx"));
        var emedf = new Emedf(Path.Combine(Program.Root, "deps", "DarkScript3", "DarkScript3", "Resources", "er-common.emedf.json"));
        var warp = gd["BonfireWarpParam"];
        // The Husks' one shop, in the copy of their script that every hub room gets and in Roundtable Hold's own.
        var shops = gd["ShopLineupParam"];
        OneShop.Apply(holdTalk, shops);
        holdTalk.Write(OutFile(outDir, "script", "talk", Roundtable + ".talkesdbnd.dcx"));

        var hubs = ReadHubs();
        rooms.Clear();
        usedFlags = FlagScan.UsedFlags(gd);
        freeFlags = new Queue<uint>(Enumerable.Range(0, 900).Select(i => DefaultFlagTop - (uint)i).Where(fl => !usedFlags.Contains(fl)));
        int order = 0, skipped = 0;
        // A map with no characters to talk to has no talk file of its own. Its residents' scripts go
        // into the common talk file instead (m00_00_00_00, where the grace menu lives), which is
        // loaded everywhere.
        const string Common = "m00_00_00_00";
        BND4 commonTalk = null;
        var addedCommon = new HashSet<string>();
        foreach (var room in hubs.GroupBy(h => h.Map))
        {
            string map = room.Key;
            string talkPath = Program.In("script", "talk", map + ".talkesdbnd.dcx");
            bool own = File.Exists(talkPath);
            if (!own) commonTalk ??= BND4.Read(Program.In("script", "talk", Common + ".talkesdbnd.dcx"));
            var msb = MSBE.Read(Program.In("map", "mapstudio", map + ".msb.dcx"));
            var talk = own ? BND4.Read(talkPath) : commonTalk;
            var emevd = EMEVD.Read(Program.In("event", map + ".emevd.dcx"));
            var addedTalk = own ? new HashSet<string>() : addedCommon;
            if (!own) Console.WriteLine($"{map}: no talk file of its own; scripts go into {Common}");
            foreach (var hub in room) BuildHub(hub, order++, msb, talk, addedTalk, emevd, source, holdTalk, emedf, warp);
            msb.Write(OutFile(outDir, "map", "mapstudio", map + ".msb.dcx"));
            if (own) talk.Write(OutFile(outDir, "script", "talk", map + ".talkesdbnd.dcx"));
            emevd.Write(OutFile(outDir, "event", map + ".emevd.dcx"));
        }
        // The hub list in the grace menu, with its lines in every language (the rooms' own place names).
        commonTalk ??= BND4.Read(Program.In("script", "talk", Common + ".talkesdbnd.dcx"));
        HubPicker.AddTo(commonTalk, rooms);
        commonTalk.Write(OutFile(outDir, "script", "talk", Common + ".talkesdbnd.dcx"));
        int holdText = RoundtablePlaceName;
        foreach (string lang in Directory.GetDirectories(Path.Combine(Program.GameDir, "msg")).Select(Path.GetFileName).Where(l => File.Exists(Program.In("msg", l, "item_dlc02.msgbnd.dcx")) && File.Exists(Program.In("msg", l, "menu_dlc02.msgbnd.dcx"))))
        {
            var lines = HubPicker.Texts(new Text(Program.In("msg", lang, "item_dlc02.msgbnd.dcx")), holdText, rooms);
            foreach (string bnd in GraceLoadouts.Gen.Builder.MenuBnds)
                GraceLoadouts.Gen.Builder.PatchFmgs(Program.In("msg", lang, bnd), OutFile(outDir, "msg", lang, bnd), fmg => fmg == "EventTextForTalk" ? lines : null);
        }


        warp.Rows = warp.Rows.OrderBy(r => r.ID).ToList();
        gd.Regulation.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "BonfireWarpParam").Bytes = warp.Write();
        gd.Regulation.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "ShopLineupParam").Bytes = shops.Write();
        gd.Regulation.Files.First(f => Path.GetFileNameWithoutExtension(f.Name) == "NpcParam").Bytes = gd["NpcParam"].Write();
        SoulsFormats.Cryptography.RegulationDecryptor.EncryptERRegulation(Path.Combine(outDir, "regulation.bin"), gd.Regulation);
        Console.WriteLine($"wrote {outDir}: {order} hubs in {hubs.Select(h => h.Map).Distinct().Count()} maps{(skipped > 0 ? $", {skipped} skipped" : "")}");
        Program.Over = null;
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

        // The Husks' body goes, and a merchant sits where their talker stood, as that same entity.
        foreach (var body in msb.Parts.Assets.Where(a => a.ModelName == HusksBody && props.Contains(a)).ToList()) msb.Parts.Assets.Remove(body);
        var seller = AsMerchant(talker);
        seller.Name = $"{seller.ModelName}_{talker.Name[(talker.Name.LastIndexOf('_') + 1)..]}";
        seller.InstanceID = talker.InstanceID;
        seller.Unk1 = talker.Unk1.DeepCopy();
        seller.Gparam = talker.Gparam.DeepCopy();
        seller.Unk8 = talker.Unk8.DeepCopy();
        seller.TileLoad = talker.TileLoad.DeepCopy();
        msb.Parts.Enemies.Remove(talker);
        msb.Parts.Enemies.Add(seller);
        if (!msb.Models.Enemies.Any(m => m.Name == seller.ModelName))
        {
            var like = msb.Models.Enemies.First();
            msb.Models.Enemies.Add(new MSBE.Model.Enemy { Name = seller.ModelName, SibPath = like.SibPath.Replace(like.Name, seller.ModelName) });
        }
    }

    // ---- the Husks as a travelling merchant who plays his instrument ---------------------------
    // Four merchants in the game play: their NpcParam rows have vowType 1 and special effect 11035
    // (Nomadic Merchants 1 and 7, the Abandoned and the Imprisoned Merchant). The Husks become a
    // copy of the first, under the Husks' own name and with the Husks' talk script and shop.
    const string HusksBody = "AEG221_527";
    const string MerchantMap = "m60_44_39_00", MerchantPart = "c3200_9000";
    const int MerchantNpc = 32001110, HusksNpc = 500020079, HusksMerchantNpc = 32009790;
    static MSBE.Part.Enemy merchantTemplate;

    /// Adds the merchant's NpcParam row (the playing merchant, named as the Husks, dropping nothing)
    /// and reads the merchant's map placement to copy from.
    static void PrepareMerchant(GameData gd)
    {
        var npc = gd["NpcParam"];
        if (npc[HusksMerchantNpc] != null) throw new Exception($"NpcParam row {HusksMerchantNpc} already exists");
        var row = new PARAM.Row(npc[MerchantNpc] ?? throw new Exception("the playing merchant's NpcParam row is missing")) { ID = HusksMerchantNpc, Name = "" };
        row.Set("nameId", npc[HusksNpc].Get<int>("nameId"));
        row.Set("itemLotId_map", -1);
        row.Set("itemLotId_enemy", -1);
        npc.Rows.Add(row);
        npc.Rows = npc.Rows.OrderBy(r => r.ID).ToList();
        merchantTemplate = MSBE.Read(Program.In("map", "mapstudio", MerchantMap + ".msb.dcx")).Parts.Enemies.First(e => e.Name == MerchantPart);
    }

    /// The merchant in the Husks' place: same spot, facing, entity, floor piece and talk script.
    static MSBE.Part.Enemy AsMerchant(MSBE.Part.Enemy husks)
    {
        var m = (MSBE.Part.Enemy)merchantTemplate.DeepCopy();
        m.Position = husks.Position;
        m.Rotation = husks.Rotation;
        m.EntityID = husks.EntityID;
        for (int i = 0; i < m.EntityGroupIDs.Length; i++) m.EntityGroupIDs[i] = 0;
        m.TalkID = husks.TalkID;
        m.NPCParamID = HusksMerchantNpc;
        m.CollisionPartName = husks.CollisionPartName;
        return m;
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
        // Pairs fan out from the middle: the first two at half the spread either side (the spots of
        // Hewg and the Husks before anyone else was added), each further pair 50 degrees outside the last.
        float Spread = landingSide ? 130 : 70;
        var landing = msb.Parts.Players.FirstOrDefault(p => p.EntityID == grace.EntityID - 970);
        var away = landing != null ? new Vector2(grace.Position.X - landing.Position.X, grace.Position.Z - landing.Position.Z) : Vector2.Zero;
        away = away.Length() > 0.5f ? Vector2.Normalize(away) : new Vector2(1, 0);
        if (landingSide) away = -away;
        float y = landingSide && landing != null ? landing.Position.Y : grace.Position.Y;
        Console.WriteLine($"  grace {grace.Position}, landing {landing?.Position.ToString() ?? "none"}");
        var spots = new Dictionary<string, Spot>();
        for (int i = 0; i < groups.Length; i++)
        {
            float a = (i % 2 == 0 ? -1 : 1) * (Spread / 2 + 50 * (i / 2)) * MathF.PI / 180;
            float Distance = i < 2 ? 5 : 5.5f;
            var d = new Vector2(away.X * MathF.Cos(a) - away.Y * MathF.Sin(a), away.X * MathF.Sin(a) + away.Y * MathF.Cos(a));
            // A part with facing f looks along (-sin f, -cos f); here it looks back at the grace, along -d.
            spots[groups[i].Key] = new Spot(new Vector3(grace.Position.X + d.X * Distance, y, grace.Position.Z + d.Y * Distance), MathF.Atan2(d.X, d.Y) * 180 / MathF.PI);
        }
        return spots;
    }

    static void BuildHub(Hub hub, int order, MSBE msb, BND4 talk, HashSet<string> addedTalk, EMEVD emevd, MSBE source, BND4 holdTalk, Emedf emedf, PARAM warp)
    {
        var grace = msb.Parts.Assets.FirstOrDefault(a => a.EntityID == hub.GraceEntity) ?? throw new Exception($"{hub.Map}: no grace with entity {hub.GraceEntity}");
        // A spot file may name only some residents; the others are placed automatically.
        var spots = AutoSpots(msb, grace, Residents, hub.LandingSide);
        var fromFile = hub.SpotFile != null ? Spot.Read(DataFile(hub.SpotFile)) : new();
        foreach (var kv in fromFile) spots[kv.Key] = kv.Value;
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
            if (g == Husks) srcProps = srcProps.Where(a => a.ModelName != HusksBody).ToList();
            var character = (MSBE.Part.Enemy)srcCharacter.DeepCopy();
            var props = srcProps.Select(a => (MSBE.Part.Asset)a.DeepCopy()).ToList();
            Place(g, character, props, spot);
            if (g == Husks) character = AsMerchant(character);

            // Lighting, draw groups and floor come from what already stands in the room: the nearest
            // fixed prop (at this height if there is one) and the nearest talk dummy.
            var fixedProps = msb.Parts.Assets.Where(a => a.EntityID == 0).ToList();
            var level = fixedProps.Where(a => MathF.Abs(a.Position.Y - spot.Position.Y) < 2).ToList();
            var localProp = (level.Count > 0 ? level : fixedProps).OrderBy(a => Vector3.Distance(a.Position, spot.Position)).First();
            var onFloor = msb.Parts.Enemies.Where(e => !string.IsNullOrEmpty(e.CollisionPartName)).ToList();
            // A talk dummy close by if there is one (every grace has one); else whoever stands nearest, often the boss.
            var dummies = onFloor.Where(e => e.ModelName == TalkDummyModel && Vector3.Distance(e.Position, spot.Position) < 15).ToList();
            var localCharacter = (dummies.Count > 0 ? dummies : onFloor).OrderBy(e => Vector3.Distance(e.Position, spot.Position)).First();
            Console.WriteLine($"{hub.Map} {hub.GraceEntity} {g.Key}: {(fromFile.ContainsKey(g.Key) ? "spot file" : "auto")}, floor {spot.Position} facing {spot.Facing:0.#}, {props.Count} props; like prop {localProp.Name} ({Vector3.Distance(localProp.Position, spot.Position):0.0} m) and {localCharacter.Name} on {localCharacter.CollisionPartName} ({Vector3.Distance(localCharacter.Position, spot.Position):0.0} m); prop floor \"{localProp.UnkPartNames[3]}\"");

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
            {
                // Its entry in Roundtable Hold's model list, or (human characters have none there) one written
                // the way this map writes its others.
                var known = source.Models.Enemies.FirstOrDefault(m => m.Name == character.ModelName);
                var like = msb.Models.Enemies.First();
                msb.Models.Enemies.Add(new MSBE.Model.Enemy { Name = character.ModelName, SibPath = known?.SibPath ?? like.SibPath.Replace(like.Name, character.ModelName) });
            }

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
        // Also which map the entry belongs to (overworld, underground, Realm of Shadow: dispMask00,
        // dispMask01 and the bit the definitions call pad1). An entry kept on the Realm of Shadow map
        // is in no list at all, because Roundtable Hold's section only exists on the main map.
        foreach (string f in new[] { "areaNo", "gridXNo", "gridZNo", "bonfireSubCategoryId", "dispMask00", "dispMask01", "pad1" }) row[f].Value = holdRow[f].Value;
        row.Set("posX", holdRow.Get<float>("posX") + IconOffset.X);
        row.Set("posY", holdRow.Get<float>("posY") + IconOffset.Y);
        row.Set("posZ", holdRow.Get<float>("posZ") + IconOffset.Z);
        row.Set("bonfireSubCategorySortId", (ushort)(holdRow.Get<ushort>("bonfireSubCategorySortId") + 10 + order));
        // The list entry is never drawn on the map: it asks for a zoom step the map never reaches.
        row.Set("dispMinZoomStep", (byte)255);
        row.Set("selectMinZoomStep", (byte)255);
        warp.Rows.Add(row);

        // The map entry: the same room again, drawn beside Roundtable Hold,
        // and only there while this room is the default hub (HubPicker sets the flag).
        uint chosen = freeFlags.Dequeue();
        int iconId = IconRowBase + roomRow.ID;
        if (warp[iconId] != null) throw new Exception($"BonfireWarpParam row {iconId} already exists");
        var icon = new PARAM.Row(row) { ID = iconId, Name = "" };
        icon.Set("eventflagId", chosen);
        // In Roundtable Hold's section like the list entry, and first below the Table of Lost Grace: an
        // entry in no section is not drawn on the map at all (seen in game). So the default hub is
        // listed twice, once at the top with its icon.
        icon.Set("bonfireSubCategorySortId", (ushort)(holdRow.Get<ushort>("bonfireSubCategorySortId") + 1));
        icon.Set("dispMinZoomStep", (byte)0);
        icon.Set("selectMinZoomStep", (byte)0);
        warp.Rows.Add(icon);
        rooms.Add(new HubPicker.Room(order, roomRow.Get<uint>("eventflagId"), chosen, roomRow.Get<int>("textId1")));
        Console.WriteLine($"{hub.Map} {hub.GraceEntity}: {characters.Count} characters, {hidden.Count} props, events {hideId} and {effectId}, travel row {rowId} to grace {roomRow.ID}, map entry {iconId} while flag {chosen} is on, residents shown by flag {hub.BossDeadFlag}");
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
