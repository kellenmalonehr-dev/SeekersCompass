using System.Text.Json;
using SoulsFormats;

namespace BuildCompass.Gen;

/// Packs EMEVD instruction args using DarkScript3's EMEDF so arg layouts come from
/// the community definitions instead of hand-counted byte offsets.
class Emedf
{
    readonly Dictionary<(int, int), int[]> argTypes = new();

    public Emedf(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var cls in doc.RootElement.GetProperty("main_classes").EnumerateArray())
        {
            int bank = cls.GetProperty("index").GetInt32();
            foreach (var ins in cls.GetProperty("instrs").EnumerateArray())
                argTypes[(bank, ins.GetProperty("index").GetInt32())] =
                    ins.GetProperty("args").EnumerateArray().Select(a => a.GetProperty("type").GetInt32()).ToArray();
        }
    }

    static int Size(int type) => type switch { 0 or 3 => 1, 1 or 4 => 2, _ => 4 };

    public EMEVD.Instruction Make(int bank, int id, params object[] args)
    {
        var types = argTypes[(bank, id)];
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        for (int i = 0; i < args.Length; i++)
        {
            // Trailing varargs repeat the last declared type.
            int t = types[Math.Min(i, types.Length - 1)];
            int size = Size(t);
            while (ms.Length % size != 0) bw.Write((byte)0);
            object v = args[i];
            switch (t)
            {
                case 0: bw.Write(Convert.ToByte(v)); break;
                case 1: bw.Write(Convert.ToUInt16(v)); break;
                case 2: bw.Write(Convert.ToUInt32(v)); break;
                case 3: bw.Write(Convert.ToSByte(v)); break;
                case 4: bw.Write(Convert.ToInt16(v)); break;
                case 5: bw.Write(Convert.ToInt32(v)); break;
                case 6: bw.Write(Convert.ToSingle(v)); break;
                default: throw new NotSupportedException($"emedf type {t}");
            }
        }
        while (ms.Length % 4 != 0) bw.Write((byte)0);
        return new EMEVD.Instruction(bank, id, ms.ToArray());
    }
}
