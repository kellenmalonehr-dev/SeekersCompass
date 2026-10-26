using System.Text;

namespace BuildCompass.Gen;

/// EzState expression bytecode (ESD conditions and command arguments): a small stack machine.
/// Opcodes follow ESDLang's documentation of the format.
static class EzState
{
    // ---- assembling -------------------------------------------------------

    /// Pushes an integer literal: small values fit in one byte, others use 0x82 + int32.
    public static void Int(List<byte> b, int v)
    {
        if (v >= -64 && v <= 63) b.Add((byte)(v + 64));
        else { b.Add(0x82); b.AddRange(BitConverter.GetBytes(v)); }
    }

    /// Calls function `id` with already-pushed args: [id] [args...] 0x84+argc.
    public static byte[] Call(int id, params int[] args)
    {
        var b = new List<byte>();
        Int(b, id);
        foreach (var a in args) Int(b, a);
        b.Add((byte)(0x84 + args.Length));
        return b.ToArray();
    }

    public static byte[] End(List<byte> b) { b.Add(0xA1); return b.ToArray(); }

    /// A complete expression that is just an integer (command arguments).
    public static byte[] Lit(int v) { var b = new List<byte>(); Int(b, v); return End(b); }

    // ---- disassembling ----------------------------------------------------

    static readonly Dictionary<byte, string> Ops = new()
    {
        [0x8C] = "+", [0x8D] = "neg", [0x8E] = "-", [0x8F] = "*", [0x90] = "/", [0x91] = "<=", [0x92] = ">=",
        [0x93] = "<", [0x94] = ">", [0x95] = "==", [0x96] = "!=", [0x98] = "&&", [0x99] = "||", [0x9A] = "!",
        [0xA1] = "end", [0xA6] = "cont", [0xB7] = "stop_if_false", [0xB8] = "ident", [0xB9] = "<B9>", [0xBA] = "<BA>",
    };

    public static string Disasm(byte[] code)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < code.Length; i++)
        {
            byte op = code[i];
            if (op <= 0x7F) sb.Append(op - 64);
            else if (op == 0x80) { sb.Append(BitConverter.ToSingle(code, i + 1)).Append('f'); i += 4; }
            else if (op == 0x81) { sb.Append(BitConverter.ToDouble(code, i + 1)).Append('d'); i += 8; }
            else if (op == 0x82) { sb.Append(BitConverter.ToInt32(code, i + 1)); i += 4; }
            else if (op >= 0x84 && op <= 0x8A) sb.Append($"call{op - 0x84}");
            else if (op >= 0xA7 && op <= 0xAE) sb.Append($"set_r{op - 0xA7}");
            else if (op >= 0xAF && op <= 0xB6) sb.Append($"get_r{op - 0xAF}");
            else if (op == 0xA5)
            {
                int j = i + 1;
                while (j + 1 < code.Length && !(code[j] == 0 && code[j + 1] == 0)) j += 2;
                sb.Append('"').Append(Encoding.Unicode.GetString(code, i + 1, j - i - 1)).Append('"');
                i = j + 1;
            }
            else sb.Append(Ops.GetValueOrDefault(op, $"<{op:X2}>"));
            sb.Append(' ');
        }
        return sb.ToString().TrimEnd();
    }
}
