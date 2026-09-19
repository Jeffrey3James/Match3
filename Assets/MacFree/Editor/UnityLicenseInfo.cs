using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MacFree.Editor
{
    /// Finds the Unity serial this machine is already activated with, so CI can
    /// reuse it.
    ///
    /// Why not a .ulf? game-ci activates with UNITY_SERIAL + UNITY_EMAIL +
    /// UNITY_PASSWORD (or a licensing server) on BOTH ubuntu and macOS — no
    /// activation script reads UNITY_LICENSE, despite an error message that
    /// still mentions it. And Unity no longer issues a .ulf for Personal via
    /// manual activation at all. The serial is the only path that works, and it
    /// covers Personal too: game-ci's entrypoint explicitly randomizes the
    /// machine id when the serial starts with "F" (a Personal serial).
    ///
    /// The activated license file holds the serial, but its plain SerialMasked
    /// field hides the last group; the full value lives base64'd in
    /// DeveloperData.
    public static class UnityLicenseInfo
    {
        /// XX-XXXX-XXXX-XXXX-XXXX-XXXX — Personal serials begin "F".
        const string SerialPattern =
            @"[A-Z0-9]{2}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}";

        /// Where the activated license lives per editor platform.
        public static string DefaultLicensePath()
        {
#if UNITY_EDITOR_WIN
            return @"C:\ProgramData\Unity\Unity_lic.ulf";
#elif UNITY_EDITOR_OSX
            return "/Library/Application Support/Unity/Unity_lic.ulf";
#else
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "unity3d", "Unity", "Unity_lic.ulf");
#endif
        }

        /// Pull the serial out of a .ulf's XML. Returns null when absent or
        /// unreadable — callers fall back to asking the user to paste it.
        public static string ExtractSerial(string ulfXml)
        {
            if (string.IsNullOrEmpty(ulfXml)) return null;

            var dev = Regex.Match(ulfXml, "<DeveloperData\\s+Value=\"([^\"]+)\"");
            if (dev.Success)
            {
                try
                {
                    byte[] raw = Convert.FromBase64String(dev.Groups[1].Value);
                    // A short binary header precedes the ASCII serial; keep the
                    // printable bytes and pattern-match rather than assume an
                    // offset.
                    var sb = new StringBuilder(raw.Length);
                    foreach (byte b in raw)
                        sb.Append(b >= 32 && b <= 126 ? (char)b : ' ');
                    var m = Regex.Match(sb.ToString(), SerialPattern);
                    if (m.Success) return m.Value;
                }
                catch (FormatException) { /* not base64 — fall through */ }
            }

            // SerialMasked hides the final group ("...-XXXX"), so it is only
            // usable if some other field happens to carry a complete serial.
            foreach (Match m in Regex.Matches(ulfXml, SerialPattern))
                if (!m.Value.EndsWith("XXXX", StringComparison.Ordinal)) return m.Value;
            return null;
        }

        /// The serial this machine is activated with, or null if it can't be
        /// read (no license file, or Unity isn't activated here).
        public static string DetectSerial(string licensePath = null)
        {
            try
            {
                string p = licensePath ?? DefaultLicensePath();
                if (!File.Exists(p)) return null;
                return ExtractSerial(File.ReadAllText(p));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// Personal serials start with "F"; game-ci randomizes the container's
        /// machine id for exactly these.
        public static bool IsPersonal(string serial)
        {
            return !string.IsNullOrEmpty(serial)
                && serial.StartsWith("F", StringComparison.OrdinalIgnoreCase);
        }
    }
}
