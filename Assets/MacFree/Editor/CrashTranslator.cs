using System;
using System.Text.RegularExpressions;

namespace MacFree.Editor
{
    /// Turns a parsed crash into a plain-English cause and an actionable next
    /// step, the way ErrorTranslator does for build failures. The rules are
    /// tuned for Unity iOS specifically — jetsam kills and watchdog timeouts
    /// are the two most common ways a Unity game dies on device, and neither
    /// looks like a crash to someone reading the raw log.
    ///
    /// Order matters: the specific rules come first, because several distinct
    /// causes surface as the same exception type.
    public static class CrashTranslator
    {
        /// Anything below this fault address is a null dereference plus a small
        /// field offset, not a wild pointer.
        const long Nullish = 0x1000;

        /// How many identical top frames mean runaway recursion rather than an
        /// ordinary call stack.
        const int RecursionRun = 8;

        public static (string cause, string hint) Diagnose(CrashReport r)
        {
            if (r == null) return ("", "");
            if (string.IsNullOrEmpty(r.RawLog))
                return ("Crash log unavailable", "");

            string type = r.ExceptionType ?? "";
            string codes = (r.ExceptionCodes ?? "").ToLowerInvariant();
            string term = (r.TerminationReason ?? "").ToLowerInvariant();
            string raw = r.RawLog ?? "";
            // TopAppFrame's system-frame filter only recognises symbol names
            // (lib*, -[, il2cpp::, ...) - a bare unsymbolicated app frame like
            // "MyGame 0x0000000102a4c000 + 64" matches none of them, so
            // without this check it would be named as the cause: hex
            // presented as if it were an answer, exactly what the spec's
            // unsymbolicated notice exists to avoid.
            string top = r.IsSymbolicated ? TopAppFrame(r) : "";

            if (codes.Contains("0x8badf00d") || term.Contains("0x8badf00d"))
                return ("iOS killed the game for taking too long to start or resume",
                    "The watchdog allows only a few seconds. Move heavy work out of "
                    + "Awake/Start, load scenes with LoadSceneAsync, and defer asset "
                    + "loading until after the first frame.");

            if (type == "EXC_RESOURCE" || term.Contains("jetsam"))
                return ("Out of memory — iOS killed the game",
                    "The game exceeded the memory iOS allows it. Lower texture import "
                    + "sizes and max resolution, use ASTC compression, compress or stream "
                    + "audio, and profile with the Memory Profiler package.");

            if (codes.Contains("0xdead10cc"))
                return ("Held a system resource while in the background",
                    "The game kept a file or database lock open after being backgrounded. "
                    + "Release file handles in OnApplicationPause.");

            if (raw.IndexOf("Library not loaded", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("Symbol not found", StringComparison.OrdinalIgnoreCase) >= 0)
                return ("A native plugin framework was missing at launch",
                    "A framework the build links against was not embedded in the IPA. "
                    + "Check that the plugin's .framework is set to embed in its Unity "
                    + "plugin importer settings, then ship again.");

            if (raw.IndexOf("IOAF code", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("gpuRestart", StringComparison.OrdinalIgnoreCase) >= 0)
                return ("The GPU hung and iOS restarted it",
                    "Usually a custom shader looping too long or oversized render "
                    + "textures. Test the scene on the oldest device you support.");

            // Apple emits a Last Exception Backtrace ONLY for an uncaught
            // Objective-C exception, so the section's presence is itself the
            // diagnosis. It has to be read here because the crashed thread of
            // such a crash is pure abort/unwind machinery: without this rule
            // the commonest third-party-SDK crash reported as a bare
            // "EXC_CRASH" over frames naming nothing the developer wrote.
            if (r.LastExceptionFrames.Count > 0)
            {
                string thrower = TopThrownFrame(r.LastExceptionFrames);
                return ("An uncaught Objective-C exception in native code",
                    (thrower == "" ? "A native plugin raised an exception nobody caught."
                        : "Thrown from " + thrower + " — a native plugin raised an exception "
                          + "nobody caught.")
                    + " These are usually a missing or invalid Info.plist entry the plugin "
                    + "requires at startup. Check that plugin's setup, and the full Last "
                    + "Exception Backtrace in the raw log below.");
            }

            if (FramesContain(r, "il2cpp::vm::Exception") || FramesContain(r, "il2cpp_codegen_raise"))
                return ("An unhandled C# exception aborted the game",
                    "The managed exception was not caught and IL2CPP aborted. "
                    + "The crashed thread's frames point at the C# method: " + top);

            if (type == "EXC_BAD_ACCESS")
            {
                if (HasRecursionRun(r))
                    return ("Stack overflow — runaway recursion",
                        "A method calls itself without a stopping condition: " + top);
                long fault = FaultAddress(r.ExceptionSubtype);
                if (fault >= 0 && fault < Nullish)
                    return ("Null reference in " + (top == "" ? "native code" : top),
                        "Something was used before it was assigned, or after it was "
                        + "destroyed. Check the object touched in " + top + ".");
            }

            // type is "" whenever the parser's outer catch fired, an .ips
            // payload had no "exception" object, or a legacy log had no
            // "Exception Type:" line. The never-throw parser makes that state
            // MORE likely, not less, and a blank cause reads as a bug in
            // MacFree rather than an unreadable log from Apple.
            return (string.IsNullOrEmpty(type) ? "Unrecognised crash log - see the raw log below" : type, "");
        }

        /// The first frame that looks like game code rather than a system
        /// library — that is the one the user can act on.
        // "CoreFoundation 0x1a30f4000 + 968816" — an image plus an offset, i.e.
        // a frame Apple never symbolicated. It names no code to go and look at.
        static readonly Regex UnsymbolicatedFrame = new Regex(
            @"\s0x[0-9a-fA-F]+\s*\+\s*\d+$", RegexOptions.Compiled);

        /// The first frame in an exception backtrace that actually names code.
        /// The frames above it are CoreFoundation's and libobjc's own throw
        /// machinery, which Apple leaves as bare addresses — reporting one of
        /// those as the thrower would point the developer at the runtime
        /// instead of at the plugin that raised the exception.
        static string TopThrownFrame(System.Collections.Generic.List<string> frames)
        {
            foreach (string f in frames)
            {
                if (UnsymbolicatedFrame.IsMatch(f)) continue;
                if (f.StartsWith("lib") || f.StartsWith("-[") || f.StartsWith("+[")) continue;
                int gap = f.IndexOf("  (", StringComparison.Ordinal);
                return gap > 0 ? f.Substring(0, gap) : f;
            }
            return "";
        }

        static string TopAppFrame(CrashReport r)
        {
            foreach (string f in r.CrashedThreadFrames)
            {
                if (f.StartsWith("lib") || f.StartsWith("-[") || f.StartsWith("+[")) continue;
                if (f.StartsWith("abort") || f.StartsWith("__") || f.StartsWith("il2cpp::")) continue;
                int gap = f.IndexOf("  (", StringComparison.Ordinal);
                return gap > 0 ? f.Substring(0, gap) : f;
            }
            return "";
        }

        static bool FramesContain(CrashReport r, string needle)
        {
            foreach (string f in r.CrashedThreadFrames)
                if (f.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// The same frame repeated many times over is recursion; a normal stack
        /// repeats a frame at most a handful of times.
        static bool HasRecursionRun(CrashReport r)
        {
            int run = 1;
            for (int i = 1; i < r.CrashedThreadFrames.Count; i++)
            {
                if (r.CrashedThreadFrames[i] == r.CrashedThreadFrames[i - 1]) run++;
                else run = 1;
                if (run >= RecursionRun) return true;
            }
            return false;
        }

        static readonly Regex Hex = new Regex(@"0x(?<v>[0-9a-fA-F]+)", RegexOptions.Compiled);

        /// The faulting address out of "KERN_INVALID_ADDRESS at 0x0000...".
        /// Returns -1 when the subtype carries no address.
        static long FaultAddress(string subtype)
        {
            if (string.IsNullOrEmpty(subtype)) return -1;
            var m = Hex.Match(subtype);
            if (!m.Success) return -1;
            long v;
            return long.TryParse(m.Groups["v"].Value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : -1;
        }
    }
}
