using SoulsFormats;

namespace BuildCompass.Gen;

/// `near <map> <x> <z> <radius> [minY maxY]`: every part and region of the map within the radius, nearest first.
static class MapNear
{
    public static void Run(string[] args)
    {
        int mi = Array.IndexOf(args, "--mod");
        if (mi >= 0) { Program.Base = args[mi + 1]; args = args.Take(mi).ToArray(); }
        var msb = MSBE.Read(Program.In("map", "mapstudio", args[0] + ".msb.dcx"));
        float x = float.Parse(args[1]), z = float.Parse(args[2]), r = float.Parse(args[3]);
        float minY = args.Length > 4 ? float.Parse(args[4]) : float.MinValue, maxY = args.Length > 5 ? float.Parse(args[5]) : float.MaxValue;
        float D(System.Numerics.Vector3 p) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
        foreach (var p in msb.Parts.GetEntries().Where(p => D(p.Position) <= r && p.Position.Y >= minY && p.Position.Y <= maxY).OrderBy(p => D(p.Position)))
            Console.WriteLine($"{p.GetType().Name,-12} {p.Name,-22} entity {p.EntityID,-10} at {p.Position.X:0.0} {p.Position.Y:0.0} {p.Position.Z:0.0} facing {p.Rotation.Y:0} dist {D(p.Position):0}");
        foreach (var p in msb.Regions.GetEntries().Where(p => D(p.Position) <= r && p.Position.Y >= minY && p.Position.Y <= maxY).OrderBy(p => D(p.Position)))
            Console.WriteLine($"region {p.GetType().Name,-14} {p.Name,-30} entity {p.EntityID,-10} at {p.Position.X:0.0} {p.Position.Y:0.0} {p.Position.Z:0.0} dist {D(p.Position):0}");
    }
}
