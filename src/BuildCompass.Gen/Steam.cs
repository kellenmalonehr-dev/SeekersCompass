using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BuildCompass.Gen;

/// Finds Steam libraries, the Elden Ring install, and a usable Oodle DLL without hard-coded paths.
static class Steam
{
    static IEnumerable<string> Libraries()
    {
        var steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)?.Replace('/', '\\');
        if (steam == null) yield break;
        yield return steam;
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    public static string FindGameDir()
    {
        foreach (var lib in Libraries().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string manifest = Path.Combine(lib, "steamapps", "appmanifest_1245620.acf"); // Elden Ring's app id
            if (!File.Exists(manifest)) continue;
            string dir = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
            string game = Path.Combine(lib, "steamapps", "common", dir, "Game");
            if (File.Exists(Path.Combine(game, "eldenring.exe"))) return game;
        }
        return null;
    }

    /// The first oo2core_9 or oo2core_8 found in any installed Steam game (searched a few levels deep).
    public static string FindOodle()
    {
        foreach (var name in new[] { "oo2core_9_win64.dll", "oo2core_8_win64.dll" })
            foreach (var lib in Libraries().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string common = Path.Combine(lib, "steamapps", "common");
                if (!Directory.Exists(common)) continue;
                var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
                var hit = Directory.EnumerateFiles(common, name, opts).FirstOrDefault();
                if (hit != null) return hit;
            }
        return null;
    }
}
