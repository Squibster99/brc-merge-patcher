// BRC Merge Patcher 1.0.0. MIT licence (see LICENSE).
// Adds BRC mods' vendor tables and lines (additions.txt) to an existing loose TreasureTable.txt for
// Divinity: Original Sin Enhanced Edition. It never merges other mods' files with each other.
// Usage: BrcMergePatcher.exe [check|patch|remove] [--game <folder>] [--file <path>] [--additions <path>] [--backup-dir <path>]
// With no command it runs "patch" and waits for Enter, so it can be double-clicked.
// Grown from the CompatProbe Phase 8 test build; the patch, check and remove logic is unchanged.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static class Program
{
    const string Version = "1.0.0";
    // Latin-1 maps every byte to one char, so unknown bytes survive a read and write unchanged.
    static readonly Encoding Bytes = Encoding.GetEncoding(28591);
    static readonly Regex Header = new Regex("^\\s*new treasuretable \"(.+?)\"");
    const string GameFolder = "Divinity Original Sin Enhanced Edition";
    const string Relative = @"Data\Public\Shared\Stats\Generated\TreasureTable.txt";

    class Additions
    {
        public string TablesBefore;
        public List<string> TableNames = new List<string>();
        public List<string> TableLines = new List<string>();
        public List<KeyValuePair<string, List<string>>> Vendor = new List<KeyValuePair<string, List<string>>>();
    }

    class Range { public int Start, End; }

    static List<string> Split(string s)
    {
        var r = new List<string>();
        int a = 0;
        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n') { r.Add(s.Substring(a, i - a + 1)); a = i + 1; }
        if (a < s.Length) r.Add(s.Substring(a));
        return r;
    }

    static Dictionary<string, Range> Tables(List<string> lines)
    {
        var r = new Dictionary<string, Range>();
        Range cur = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var m = Header.Match(lines[i]);
            if (!m.Success) continue;
            if (cur != null) cur.End = i;
            cur = new Range { Start = i, End = lines.Count };
            if (!r.ContainsKey(m.Groups[1].Value)) r[m.Groups[1].Value] = cur;
        }
        return r;
    }

    static int FindBlock(List<string> lines, Range t, List<string> block)
    {
        for (int i = t.Start; i + block.Count <= t.End; i++)
        {
            int k = 0;
            while (k < block.Count && lines[i + k].Trim() == block[k]) k++;
            if (k == block.Count) return i;
        }
        return -1;
    }

    static Additions Load(string path)
    {
        var a = new Additions();
        List<string> target = null;
        foreach (var raw in File.ReadAllLines(path, Bytes))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l[0] == '#') continue;
            if (l.StartsWith("@tables-before "))
            {
                a.TablesBefore = l.Substring(15).Trim(); target = a.TableLines;
            }
            else if (l.StartsWith("@append "))
            {
                target = new List<string>();
                a.Vendor.Add(new KeyValuePair<string, List<string>>(l.Substring(8).Trim(), target));
            }
            else
            {
                if (target == null) throw new InvalidDataException("additions file: text before the first @ line");
                var m = Header.Match(l);
                if (m.Success && target == a.TableLines) a.TableNames.Add(m.Groups[1].Value);
                target.Add(l);
            }
        }
        if (a.TableNames.Count == 0 && a.Vendor.Count == 0) throw new InvalidDataException("additions file is empty");
        return a;
    }

    // 0 = none of ours present, 1 = some, 2 = all that can be present
    static int State(string text, Additions a)
    {
        var lines = Split(text);
        var t = Tables(lines);
        int have = 0, want = 0;
        foreach (var n in a.TableNames) { want++; if (t.ContainsKey(n)) have++; }
        foreach (var v in a.Vendor)
        {
            if (!t.ContainsKey(v.Key)) continue;
            want++;
            if (FindBlock(lines, t[v.Key], v.Value) >= 0) have++;
        }
        return have == 0 ? 0 : have == want ? 2 : 1;
    }

    static string Remove(string text, Additions a)
    {
        var lines = Split(text);
        var t = Tables(lines);
        var drop = new HashSet<int>();
        foreach (var n in a.TableNames)
            if (t.ContainsKey(n))
                for (int i = t[n].Start; i < t[n].End; i++) drop.Add(i);
        foreach (var v in a.Vendor)
        {
            if (!t.ContainsKey(v.Key)) continue;
            int at = FindBlock(lines, t[v.Key], v.Value);
            if (at >= 0) for (int i = 0; i < v.Value.Count; i++) drop.Add(at + i);
        }
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Count; i++) if (!drop.Contains(i)) sb.Append(lines[i]);
        return sb.ToString();
    }

    static string Apply(string text, Additions a, List<string> warnings)
    {
        var lines = Split(text);
        var t = Tables(lines);
        int crlf = lines.Count(l => l.EndsWith("\r\n"));
        string eol = crlf * 2 >= lines.Count ? "\r\n" : "\n";
        var insert = new Dictionary<int, List<string>>();
        Action<int, IEnumerable<string>> add = (i, ls) =>
        {
            if (!insert.ContainsKey(i)) insert[i] = new List<string>();
            insert[i].AddRange(ls.Select(x => x + eol));
        };
        foreach (var v in a.Vendor)
        {
            if (!t.ContainsKey(v.Key))
            {
                warnings.Add("Vendor table not found, skipped: " + v.Key);
                continue;
            }
            int end = t[v.Key].End;
            while (end - 1 > t[v.Key].Start && lines[end - 1].Trim().Length == 0) end--;
            if (!lines[end - 1].EndsWith("\n")) lines[end - 1] += eol;
            add(end, v.Value);
        }
        if (a.TableLines.Count > 0)
        {
            int at = lines.Count;
            if (a.TablesBefore != null && t.ContainsKey(a.TablesBefore)) at = t[a.TablesBefore].Start;
            else warnings.Add("Anchor table not found; new tables added at the end: " + a.TablesBefore);
            if (at == lines.Count && at > 0 && !lines[at - 1].EndsWith("\n")) lines[at - 1] += eol;
            add(at, a.TableLines);
        }
        var sb = new StringBuilder(text.Length + 40000);
        for (int i = 0; i <= lines.Count; i++)
        {
            if (insert.ContainsKey(i)) foreach (var l in insert[i]) sb.Append(l);
            if (i < lines.Count) sb.Append(lines[i]);
        }
        return sb.ToString();
    }

    static string Sha(string path)
    {
        using (var s = SHA256.Create())
        using (var f = File.OpenRead(path))
            return BitConverter.ToString(s.ComputeHash(f)).Replace("-", "");
    }

    // Steam folders: the registry install path, then every library listed in libraryfolders.vdf.
    static List<string> SteamLibraries()
    {
        var roots = new List<string>();
        foreach (var key in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
        {
            var p = Registry.GetValue(key, "InstallPath", null) as string;
            if (p != null) roots.Add(p);
        }
        var u = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (u != null) roots.Add(u.Replace('/', '\\'));
        var libs = new List<string>(roots);
        foreach (var r in roots)
        {
            var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        return libs.Select(x => x.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string FindGame()
    {
        foreach (var lib in SteamLibraries())
        {
            var g = Path.Combine(lib, "steamapps", "common", GameFolder);
            if (Directory.Exists(g)) return g;
        }
        return null;
    }

    static string Arg(List<string> a, string name)
    {
        int i = a.IndexOf(name);
        return i >= 0 && i + 1 < a.Count ? a[i + 1] : null;
    }

    static int Run(List<string> args)
    {
        Console.WriteLine("BRC Merge Patcher " + Version);
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string cmd = args.Count > 0 && !args[0].StartsWith("--") ? args[0] : "patch";
        if (cmd != "check" && cmd != "patch" && cmd != "remove") { Console.WriteLine("Unknown command: " + cmd); return 2; }
        var additions = Load(Arg(args, "--additions") ?? Path.Combine(exeDir, "additions.txt"));
        string target = Arg(args, "--file");
        if (target == null)
        {
            string game = Arg(args, "--game") ?? FindGame();
            if (game == null) { Console.WriteLine("Couldn't find the game. Run again with --game \"<game folder>\". Nothing was changed."); return 2; }
            target = Path.Combine(game, Relative);
        }
        if (cmd != "check" && Process.GetProcessesByName("EoCApp").Length > 0)
        {
            Console.WriteLine("Divinity is running. Close the game and run this again. Nothing was changed.");
            return 3;
        }
        if (!File.Exists(target))
        {
            Console.WriteLine("No loose TreasureTable.txt at: " + target);
            Console.WriteLine("That's normal if no other mod has added one. Use the ready-made TreasureTable download instead");
            Console.WriteLine("(see the README). This version only adds to an existing file. Nothing was changed.");
            return 4;
        }
        string text = File.ReadAllText(target, Bytes);
        int state = State(text, additions);
        Console.WriteLine("File: " + target);
        Console.WriteLine("BRC additions: " + (state == 0 ? "not present" : state == 1 ? "partly present" : "present"));
        if (cmd == "check") return 0;
        if (cmd == "patch" && state == 2) { Console.WriteLine("Already patched. Nothing was changed."); return 0; }
        if (cmd == "remove" && state == 0) { Console.WriteLine("Nothing to remove. Nothing was changed."); return 0; }

        string baseText = state == 0 ? text : Remove(text, additions);
        var warnings = new List<string>();
        string result = baseText;
        if (cmd == "patch")
        {
            result = Apply(baseText, additions, warnings);
            if (State(result, additions) != 2) throw new InvalidOperationException("self-check failed: additions not all present");
            if (Remove(result, additions) != baseText) throw new InvalidOperationException("self-check failed: original content altered");
        }
        string backups = Arg(args, "--backup-dir") ?? Path.Combine(exeDir, "Backups");
        Directory.CreateDirectory(backups);
        string hash = Sha(target);
        string backup = Path.Combine(backups, "TreasureTable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + hash.Substring(0, 8) + ".txt");
        File.Copy(target, backup, true);
        if (Sha(backup) != hash) throw new IOException("backup copy did not verify");
        string temp = target + ".brc-tmp";
        File.WriteAllText(temp, result, Bytes);
        File.Replace(temp, target, null);
        if (File.ReadAllText(target, Bytes) != result) throw new IOException("written file did not verify; restore the backup: " + backup);
        foreach (var w in warnings) Console.WriteLine("Note: " + w);
        Console.WriteLine((cmd == "patch" ? "Patched." : "Removed.") + " Backup of the previous file: " + backup);
        return 0;
    }

    static int Main(string[] argv)
    {
        var args = argv.ToList();
        int code;
        try { code = Run(args); }
        catch (Exception e) { Console.WriteLine("Stopped: " + e.Message); code = 1; }
        if (argv.Length == 0) { Console.WriteLine("Press Enter to close."); Console.ReadLine(); }
        return code;
    }
}
