using System.Numerics;
using SoulsFormats;

namespace BuildCompass.Gen;

/// Puts items that are inside a location (ruin, cave, catacomb...) onto that location's map icon,
/// in a row of badges along its lower-right corner, the way NPC marks sit on locations. Slots taken
/// by a vanilla NPC mark are skipped so items line up beside the NPC instead of covering it.
class LocationSnap
{
    // Small vanilla markers (NPCs, spirit springs...) that aren't locations.
    static readonly HashSet<int> Markers = new() { 80, 83, 84, 85, 87, 88 };
    const int NpcIcon = 80;

    // Open-world items this close to a location belong to it. Dungeon interiors (m30-m4x) always
    // belong to the location their entrance converts to, if one is reasonably near.
    const float SnapRadius = 70, DungeonRadius = 400;
    // Badge row, in metres from the location's anchor (+X east, +Z north).
    const float FirstX = 35, FirstZ = -30, Step = 28;
    const int PerRow = 5;
    const float NpcClear = 22; // NPC marks and grace icons

    readonly List<(int id, int space, Vector2 xz)> locations = new();
    readonly List<(int space, Vector2 xz)> npcs = new();
    readonly Dictionary<int, int> nextSlot = new();

    public int Snapped;
    public int Locations => nextSlot.Count;

    /// Graces (BonfireWarpParam) draw above every map mark, so their spots are skipped like NPC marks.
    public LocationSnap(IEnumerable<PARAM.Row> vanillaPoints, IEnumerable<PARAM.Row> graces)
    {
        static (int space, Vector2 xz) At(PARAM.Row r) => Builder.MapPos(r.Get<byte>("areaNo"), r.Get<byte>("gridXNo"), r.Get<byte>("gridZNo"),
            new Vector3(r.Get<float>("posX"), 0, r.Get<float>("posZ")));
        foreach (var r in vanillaPoints)
        {
            var at = At(r);
            int icon = r.Get<int>("iconId");
            if (icon == NpcIcon) npcs.Add(at);
            else if (!Markers.Contains(icon)) locations.Add((r.ID, at.space, at.xz));
        }
        foreach (var r in graces) npcs.Add(At(r));
    }

    /// The local position to draw this item's mark at.
    public Vector3 Place(Placement p)
    {
        var me = Builder.MapPos(p.Area, p.GridX, p.GridZ, p.Pos);
        var near = locations.Where(l => l.space == me.space)
            .Select(l => (l, d: Vector2.Distance(l.xz, me.xz))).OrderBy(x => x.d).FirstOrDefault();
        bool dungeon = p.Area >= 30 && p.Area < 60;
        if (near.l.id == 0 || near.d > (dungeon ? DungeonRadius : SnapRadius)) return p.Pos;

        var target = NextFreeSlot(near.l);
        Snapped++;
        // MapPos is a pure translation of local coordinates, so shift the local position by the same delta.
        return p.Pos + new Vector3(target.X - me.xz.X, 0, target.Y - me.xz.Y);
    }

    Vector2 NextFreeSlot((int id, int space, Vector2 xz) loc)
    {
        int k = nextSlot.GetValueOrDefault(loc.id);
        while (true)
        {
            var slot = loc.xz + new Vector2(FirstX + Step * (k % PerRow), FirstZ - Step * (k / PerRow));
            k++;
            if (npcs.Any(n => n.space == loc.space && Vector2.Distance(n.xz, slot) < NpcClear)) continue;
            nextSlot[loc.id] = k;
            return slot;
        }
    }
}
