using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MacFree.Editor
{
    /// Raw crash logs, cached on disk beside the settings. Two reasons: the
    /// crash window would otherwise re-fetch every log on every open, and
    /// Apple prunes TestFlight feedback after about 90 days — once a log is
    /// cached the user keeps it. Submission ids are immutable, so a cached
    /// entry never needs invalidating.
    public static class CrashCache
    {
        // Windows device names (CON, NUL, COM1, ...) swallow reads and writes
        // even with an extension appended — NUL.txt is not guaranteed to be a
        // real file, it can still resolve to the null device. An id that
        // collides with one must be disambiguated, not written verbatim.
        static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        static string Dir(string root)
        {
            if (string.IsNullOrEmpty(root))
                root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "UserSettings", "MacFree", "crashes");
        }

        /// Submission ids come from a remote API. Reduce one to a bare file
        /// name so a separator or ".." in it can never write outside the cache.
        static string SafeName(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id ?? "")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            string name = sb.ToString();
            if (name.Length == 0) return "unnamed";
            if (ReservedDeviceNames.Contains(name)) return "_" + name;
            return name;
        }

        /// The cached log, or null when this submission has never been fetched.
        public static string Read(string id, string root = null)
        {
            string f = Path.Combine(Dir(root), SafeName(id) + ".txt");
            if (!File.Exists(f)) return null;
            try { return File.ReadAllText(f); }
            catch { return null; }
        }

        public static void Write(string id, string text, string root = null)
        {
            if (text == null) return;
            string dir = Dir(root);
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, SafeName(id) + ".txt"), text);
            }
            catch
            {
                // A cache write failing is not worth interrupting the user;
                // the log was already fetched and is on screen.
            }
        }
    }
}
