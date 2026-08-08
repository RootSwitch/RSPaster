// Persisted preferences: %APPDATA%\RSPaster\settings.ini
//
// Deliberately plain key=value rather than JSON. There is no in-box JSON
// parser that does not pull in another assembly reference, and a hand-rolled
// one for seven scalar fields is risk with no return. This file is also
// something a person can read and edit.
//
// What is NOT stored: the contents of the text box, ever. It routinely holds
// passwords and console commands, so it never touches disk - not as a setting,
// not as a recent-items list, not as a crash backup.
//
// C# 5 only (in-box csc).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RSPaster
{
    public class Settings
    {
        public string Theme = "classic";
        public int StartDelaySeconds = 3;
        public int KeyDelayMs = 15;
        public bool LineDelayEnabled;
        public int LineDelaySeconds = 15;
        public bool EnterAtEnd;
        public bool ClearAfter;
        public bool AlwaysOnTop = true;
        public bool UnicodeMode;
        public int WindowWidth;
        public int WindowHeight;

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RSPaster");
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (IOException) { return s; }
            catch (UnauthorizedAccessException) { return s; }

            s.Theme = Str(kv, "theme", s.Theme);
            s.StartDelaySeconds = Int(kv, "startDelaySeconds", s.StartDelaySeconds, 0, 60);
            s.KeyDelayMs = Int(kv, "keyDelayMs", s.KeyDelayMs, 0, 500);
            s.LineDelayEnabled = Bool(kv, "lineDelayEnabled", s.LineDelayEnabled);
            s.LineDelaySeconds = Int(kv, "lineDelaySeconds", s.LineDelaySeconds, 0, 600);
            s.EnterAtEnd = Bool(kv, "enterAtEnd", s.EnterAtEnd);
            s.ClearAfter = Bool(kv, "clearAfter", s.ClearAfter);
            s.AlwaysOnTop = Bool(kv, "alwaysOnTop", s.AlwaysOnTop);
            s.UnicodeMode = Bool(kv, "unicodeMode", s.UnicodeMode);
            s.WindowWidth = Int(kv, "windowWidth", 0, 0, 10000);
            s.WindowHeight = Int(kv, "windowHeight", 0, 0, 10000);
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# RSPaster settings. The pasted text is never stored here.");
                Write(sb, "theme", Theme);
                Write(sb, "startDelaySeconds", StartDelaySeconds.ToString(CultureInfo.InvariantCulture));
                Write(sb, "keyDelayMs", KeyDelayMs.ToString(CultureInfo.InvariantCulture));
                Write(sb, "lineDelayEnabled", LineDelayEnabled ? "true" : "false");
                Write(sb, "lineDelaySeconds", LineDelaySeconds.ToString(CultureInfo.InvariantCulture));
                Write(sb, "enterAtEnd", EnterAtEnd ? "true" : "false");
                Write(sb, "clearAfter", ClearAfter ? "true" : "false");
                Write(sb, "alwaysOnTop", AlwaysOnTop ? "true" : "false");
                Write(sb, "unicodeMode", UnicodeMode ? "true" : "false");
                Write(sb, "windowWidth", WindowWidth.ToString(CultureInfo.InvariantCulture));
                Write(sb, "windowHeight", WindowHeight.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (IOException) { /* a settings file we cannot write is not worth a dialog */ }
            catch (UnauthorizedAccessException) { }
        }

        static void Write(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').AppendLine(value);
        }

        static string Str(Dictionary<string, string> kv, string key, string fallback)
        {
            string v;
            return kv.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        static int Int(Dictionary<string, string> kv, string key, int fallback, int min, int max)
        {
            string v;
            int n;
            if (!kv.TryGetValue(key, out v)) return fallback;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return fallback;
            if (n < min) return min;
            if (n > max) return max;
            return n;
        }

        static bool Bool(Dictionary<string, string> kv, string key, bool fallback)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) return fallback;
            return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
        }
    }
}
