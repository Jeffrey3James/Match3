using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SimpleJSON;

namespace MacFree.Editor
{
    /// Turns Apple's crash log text into the handful of fields that actually
    /// answer "why did it crash". Two formats exist and both arrive from the
    /// same endpoint: the modern JSON `.ips` (iOS 15+) and the legacy `.crash`
    /// plain text. Format is detected from content, never from a filename.
    ///
    /// Nothing here throws: a crash log that cannot be parsed must still
    /// produce a row the user can open and read raw.
    public static class CrashLogParser
    {
        // How many frames of the crashed thread to keep. The cause is
        // essentially always in the top few; the rest is in the raw log.
        const int MaxFrames = 20;

        public static CrashReport Parse(CrashSubmission meta, string rawLog)
        {
            var r = new CrashReport
            {
                Id = meta.Id,
                CreatedDate = meta.CreatedDate,
                DeviceModel = meta.DeviceModel,
                OsVersion = meta.OsVersion,
                AppUptimeMs = meta.AppUptimeMs,
                DiskBytesAvailable = meta.DiskBytesAvailable,
                BatteryPercentage = meta.BatteryPercentage,
                Comment = meta.Comment,
                RawLog = rawLog,
            };
            if (string.IsNullOrEmpty(rawLog)) return r;

            try
            {
                if (rawLog.TrimStart().StartsWith("{")) ParseIps(r, rawLog);
                else ParseLegacy(r, rawLog);
            }
            catch (Exception)
            {
                // A format Apple changed under us is not worth an error dialog;
                // the raw log is still shown, and the diagnosis simply stays empty.
            }
            return r;
        }

        static void ParseIps(CrashReport r, string rawLog)
        {
            // Header line, then payload. Some logs arrive as a single document.
            string header = null, payloadText = rawLog;
            int nl = rawLog.IndexOf('\n');
            if (nl > 0 && rawLog.Substring(nl + 1).TrimStart().StartsWith("{"))
            {
                header = rawLog.Substring(0, nl);
                payloadText = rawLog.Substring(nl + 1);
            }

            // A malformed header must cost only BuildNumber, never the payload
            // below it -- so its parse is isolated in its own try/catch. Two
            // distinct failures live here: JSON.Parse itself can throw on a
            // header truncated mid-token (e.g. cut off at a byte boundary),
            // and even a header that parses cleanly can come back as
            // something other than an object (SimpleJSON's non-object
            // indexer returns a bare null, unlike JSONObject's safe
            // lazy-creator), which would NRE on ["build_version"] without
            // the IsObject check.
            if (header != null)
            {
                try
                {
                    var h = JSON.Parse(header);
                    if (h != null && h.IsObject) r.BuildNumber = h["build_version"].Value;
                }
                catch (Exception) { }
            }

            var p = JSON.Parse(payloadText);
            if (p == null) return;

            var ex = p["exception"];
            r.ExceptionType = ex["type"].Value;
            r.Signal = ex["signal"].Value;
            r.ExceptionSubtype = ex["subtype"].Value;
            r.ExceptionCodes = ex["codes"].Value;

            var term = p["termination"];
            string ns = term["namespace"].Value;
            string ind = term["indicator"].Value;
            r.TerminationReason = string.IsNullOrEmpty(ind) ? ns : (ns + " " + ind).Trim();

            var images = p["usedImages"].AsArray;
            bool anySymbol = false;

            // Symbolication is a property of the LOG, not of the crashed
            // thread. A SIGABRT's triggered thread is pure abort/unwind
            // machinery carrying no symbols even when the app's own frames
            // elsewhere are fully symbolicated - judging the log by that one
            // thread told users their build shipped without debug symbols and
            // sent them to change build engines for nothing. Scan every
            // thread; render only the ones worth showing.
            var threads = p["threads"].AsArray;
            if (threads != null)
                foreach (JSONNode t in threads)
                    RenderFrames(t["frames"].AsArray, images, null, ref anySymbol);

            RenderFrames(p["lastExceptionBacktrace"].AsArray, images,
                r.LastExceptionFrames, ref anySymbol);

            var thread = TriggeredThread(p);
            if (thread != null)
                RenderFrames(thread["frames"].AsArray, images,
                    r.CrashedThreadFrames, ref anySymbol);

            r.IsSymbolicated = anySymbol;
        }

        /// Renders one IPS frame array into `into` (pass null to scan for
        /// symbols without keeping the frames) and records whether any frame
        /// belonging to the app itself carried a symbol. Apple symbolicates
        /// system libraries server-side whether or not the customer uploaded
        /// symbols for their OWN binary, so a symbol on a system frame must
        /// never mark the crash symbolicated - that is the exact case this
        /// flag exists to catch.
        static void RenderFrames(JSONArray frames, JSONArray images,
            List<string> into, ref bool anySymbol)
        {
            if (frames == null) return;
            foreach (JSONNode f in frames)
            {
                if (into != null && into.Count >= MaxFrames) break;
                string image = "";
                int idx = f["imageIndex"].AsInt;
                if (images != null && idx >= 0 && idx < images.Count)
                    image = images[idx]["name"].Value;

                string symbol = f["symbol"].Value;
                if (string.IsNullOrEmpty(symbol))
                {
                    if (into != null) into.Add(image + " + " + f["imageOffset"].AsLong);
                    continue;
                }
                if (!LooksLikeSystemImage(image)) anySymbol = true;
                if (into != null) into.Add(Frame(symbol));
            }
        }

        // "0   MyGame   0x0000000102a4c000 PlayerController_Update_m1A2B3C + 64"
        static readonly Regex LegacyFrame = new Regex(
            @"^\s*\d+\s+(?<image>\S+)\s+0x[0-9a-fA-F]+\s+(?<symbol>.+?)\s*$",
            RegexOptions.Compiled);

        // A frame whose "symbol" is just another address is an unsymbolicated
        // frame, not a function called `0x1029abcd`.
        static readonly Regex BareAddress = new Regex(
            @"^0x[0-9a-fA-F]+(\s*\+\s*\d+)?$", RegexOptions.Compiled);

        static void ParseLegacy(CrashReport r, string rawLog)
        {
            string[] lines = rawLog.Replace("\r\n", "\n").Split('\n');
            bool inCrashedThread = false;
            bool inLastException = false;
            bool anySymbol = false;

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();

                // Both frame blocks end at their first blank line — what
                // follows belongs to another thread, or back to the header.
                if (line.Trim().Length == 0)
                {
                    inCrashedThread = false;
                    inLastException = false;
                    continue;
                }

                if (line.StartsWith("Last Exception Backtrace:"))
                {
                    inLastException = true;
                    continue;
                }

                if (line.Contains("Crashed:") && line.TrimStart().StartsWith("Thread "))
                {
                    inCrashedThread = true;
                    continue;
                }

                var fm = LegacyFrame.Match(line);
                if (fm.Success)
                {
                    string image = fm.Groups["image"].Value;
                    string symbol = fm.Groups["symbol"].Value;
                    bool bare = BareAddress.IsMatch(symbol);

                    // See the matching comment in ParseIps: symbolication is a
                    // property of the whole log, and a symbolicated system
                    // frame (Apple's own doing) must not mask a bare app frame.
                    if (!bare && !LooksLikeSystemImage(image)) anySymbol = true;

                    string rendered = bare
                        ? image + " " + symbol
                        : Frame(StripOffset(symbol));
                    if (inCrashedThread)
                    {
                        if (r.CrashedThreadFrames.Count < MaxFrames)
                            r.CrashedThreadFrames.Add(rendered);
                    }
                    else if (inLastException && r.LastExceptionFrames.Count < MaxFrames)
                    {
                        r.LastExceptionFrames.Add(rendered);
                    }
                    continue;
                }

                string v;
                if ((v = After(line, "Exception Type:")) != null) SplitExceptionType(r, v);
                else if ((v = After(line, "Exception Subtype:")) != null) r.ExceptionSubtype = v;
                else if ((v = After(line, "Exception Codes:")) != null) r.ExceptionCodes = v;
                else if ((v = After(line, "Termination Reason:")) != null) r.TerminationReason = v;
                else if ((v = After(line, "Version:")) != null && r.BuildNumber == "")
                {
                    // Apple writes "<marketing version> (<build>)", e.g.
                    // "1.0 (334323)" for the build App Store Connect lists as
                    // 1.0 (334323). The build is the part that differs between
                    // two crashes of the same release, so it is the half worth
                    // showing; a log without parentheses gets used whole.
                    int open = v.IndexOf('(');
                    int close = v.LastIndexOf(')');
                    r.BuildNumber = open >= 0 && close > open
                        ? v.Substring(open + 1, close - open - 1).Trim()
                        : v.Trim();
                }
            }
            r.IsSymbolicated = anySymbol;
        }

        /// The value after a `Key:` prefix, or null when the line is not that key.
        static string After(string line, string key)
        {
            if (!line.StartsWith(key)) return null;
            return line.Substring(key.Length).Trim();
        }

        /// "EXC_BAD_ACCESS (SIGSEGV)" -> type + signal.
        static void SplitExceptionType(CrashReport r, string value)
        {
            int open = value.IndexOf('(');
            if (open < 0) { r.ExceptionType = value.Trim(); return; }
            r.ExceptionType = value.Substring(0, open).Trim();
            r.Signal = value.Substring(open + 1).TrimEnd(')', ' ').Trim();
        }

        /// "PlayerController_Update_m1A2B3C + 64" -> the symbol without its
        /// byte offset, so the demangler sees a clean name.
        static string StripOffset(string symbol)
        {
            int plus = symbol.LastIndexOf(" + ", StringComparison.Ordinal);
            return plus > 0 ? symbol.Substring(0, plus).Trim() : symbol.Trim();
        }

        /// A cheap but effective stand-in for matching the frame's image
        /// against the process name: Apple's own binaries are what iOS ships,
        /// and they all present this way in a crash log, while a Unity app's
        /// own image never does.
        static bool LooksLikeSystemImage(string image)
        {
            if (string.IsNullOrEmpty(image)) return false;
            return image.StartsWith("lib", StringComparison.OrdinalIgnoreCase)
                || image.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
                || image.IndexOf("/usr/lib", StringComparison.OrdinalIgnoreCase) >= 0
                || image.IndexOf("/System/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static JSONNode TriggeredThread(JSONNode payload)
        {
            var threads = payload["threads"].AsArray;
            if (threads == null || threads.Count == 0) return null;
            foreach (JSONNode t in threads)
                if (t["triggered"].AsBool) return t;
            int fi = payload["faultingThread"].AsInt;
            return fi >= 0 && fi < threads.Count ? threads[fi] : threads[0];
        }

        /// A demangled frame keeps its original symbol in parentheses: the
        /// Class_Method split is a heuristic, and a wrong guess must never
        /// destroy the only text the user could search their code for.
        static string Frame(string symbol)
        {
            string pretty = CrashSymbols.Demangle(symbol);
            return pretty == symbol ? symbol : pretty + "  (" + symbol + ")";
        }
    }
}
