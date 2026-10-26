using SoulsFormats;

namespace BuildCompass.Gen;

/// Prints DCX-compressed text files (e.g. eventflag alloc lists).
static class Dump
{
    public static void Run(string[] files)
    {
        foreach (var f in files)
            Console.Write(System.Text.Encoding.UTF8.GetString(DCX.Decompress(f).ToArray()));
    }
}
