using SoulsFormats;

namespace BuildCompass.Gen;

/// Loads vanilla params and text from the game folder.
class GameData
{
    public BND4 Regulation;
    public Dictionary<string, PARAM> Params = new();
    public ulong Version;
    readonly Dictionary<string, PARAMDEF> defs = new();

    public static GameData Load(string regulationPath)
    {
        var gd = new GameData { Regulation = SoulsFormats.Cryptography.RegulationDecryptor.DecryptERRegulation(regulationPath) };
        gd.Version = ulong.Parse(gd.Regulation.Version);
        string defDir = Path.Combine(Program.Root, "deps", "Paramdex", "ER", "Defs");
        foreach (var f in Directory.GetFiles(defDir, "*.xml"))
        {
            var def = PARAMDEF.XmlDeserialize(f, true, false);
            gd.defs[def.ParamType] = def;
        }
        foreach (var file in gd.Regulation.Files.Where(f => f.Name.EndsWith(".param")))
        {
            string name = Path.GetFileNameWithoutExtension(file.Name);
            var p = PARAM.Read(file.Bytes);
            try
            {
                if (gd.defs.TryGetValue(p.ParamType, out var def) && p.ApplyRegulationVersionedParamdefCarefully(def, gd.Version))
                    gd.Params[name] = p;
            }
            catch (InvalidDataException e)
            {
                // Paramdex lags the game for SpEffectParam: a patch started using reserved bitfield
                // bits. The layout is still right, so read it (read-only) with the padding check off.
                if (name == "SpEffectParam" && gd.defs.TryGetValue(p.ParamType, out var spDef))
                {
                    PARAM.Row.IgnoreOrphanedBits = true;
                    try { p.ApplyRegulationVersionedParamdef(spDef, gd.Version); gd.Params[name] = p; }
                    finally { PARAM.Row.IgnoreOrphanedBits = false; }
                    Console.Error.WriteLine($"{name}: read-only, padding check relaxed");
                }
                else Console.Error.WriteLine($"skip {name}: {e.Message}");
            }
        }
        return gd;
    }

    public PARAM this[string name] => Params[name];

    public PARAMDEF Def(string paramType) => defs[paramType];

    public byte[] RegulationVersionBytes() => null;
}

static class RowExt
{
    public static T Get<T>(this PARAM.Row row, string field) => (T)Convert.ChangeType(row[field].Value, typeof(T));
    public static void Set(this PARAM.Row row, string field, object value)
    {
        var cell = row[field];
        cell.Value = Convert.ChangeType(value, cell.Value.GetType());
    }
}
