using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MacFree.Editor
{
    /// Browses the TestFlight crashes Apple collected for this app: a list on
    /// the left, the diagnosis and the crashed thread on the right. Like
    /// MacFreeWindow, remote work runs as a Task pumped by the editor loop so
    /// the UI never blocks.
    public class MacFreeCrashWindow : EditorWindow
    {
        const int FetchLimit = 25;
        // IMGUI renders a text element as a single mesh and silently cuts it
        // off (logging "String too long for TextMeshGenerator") past roughly
        // 16k characters; a real .ips with all threads and the binary-images
        // table is routinely tens to hundreds of KB.
        const int RawLogPreviewLimit = 12000;

        const float ListWidth = 250f;

        /// Height of the raw log's own scroll region. Bounded on purpose: an
        /// unbounded log pushes every other section off the pane.
        const float RawLogViewHeight = 260f;

        MacFreeSettings s;
        List<CrashReport> reports = new List<CrashReport>();
        int selected = -1;
        Task activeTask;
        // Short in-progress text ("Fetching crash 3 of 12...") - fits the
        // toolbar row. Long, deliberately-worded copy (empty list / no app
        // record) goes in `status` instead, rendered as a wrapping HelpBox;
        // the toolbar is one fixed-height line with no wrapping and would
        // clip either string mid-sentence.
        string progress = "";
        string status = "";
        string error = "";
        bool rawFold;
        Vector2 listScroll, detailScroll, rawScroll;

        // GetWindow<T>() calls OnEnable synchronously the first time it creates
        // the window - before Open() gets a chance to assign w.s below. Without
        // this, that first OnEnable falls through to MacFreeSettings.Load() and
        // kicks off the auto-refresh against a freshly-loaded (possibly stale)
        // object instead of the caller's live one.
        static MacFreeSettings pendingSettings;

        public static void Open(MacFreeSettings settings)
        {
            pendingSettings = settings;
            var w = GetWindow<MacFreeCrashWindow>("Crashes");
            pendingSettings = null;
            w.minSize = new Vector2(720, 420);
            w.s = settings;
            w.Show();
        }

        void OnEnable()
        {
            if (s == null) s = pendingSettings ?? MacFreeSettings.Load();
            EditorApplication.update += Pump;
            if (reports.Count == 0 && activeTask == null) Start(Refresh());
        }

        void OnDisable() { EditorApplication.update -= Pump; }

        void Pump()
        {
            if (activeTask == null || !activeTask.IsCompleted) return;
            if (activeTask.IsFaulted)
            {
                error = ErrorTranslator.Translate(activeTask.Exception.GetBaseException());
                progress = "";
            }
            activeTask = null;
            Repaint();
        }

        void Start(Task t) { error = ""; activeTask = t; }

        AscApi Asc() { return new AscApi(() => AscJwt.GetCached(s)); }

        /// MacFreeWindow and this window can hold two SEPARATE MacFreeSettings
        /// instances - EditorWindow fields don't survive a domain reload, so
        /// OnEnable on each window calls Load() independently. Save() writes
        /// the ENTIRE settings file, so saving this window's in-memory `s`
        /// directly would silently revert whatever the other window wrote to
        /// disk since (most importantly the ASC credentials, including the
        /// once-downloadable .p8 content cached in AscP8Base64). Reload the
        /// current file, change only the field(s) this window owns, and save
        /// that - never `s` itself.
        static void Persist(Action<MacFreeSettings> apply)
        {
            var d = MacFreeSettings.Load();
            apply(d);
            d.Save();
        }

        async Task Refresh()
        {
            status = "";
            progress = "Loading crashes...";
            // One AscApi per crash would allocate a fresh, never-disposed
            // HttpClient (and its own connection pool) per submission - in a
            // 25-crash refresh, 27 of them, in a process that stays alive for
            // days across many refreshes. Hoist a single instance instead.
            var asc = Asc();
            if (string.IsNullOrEmpty(s.AppId))
            {
                s.AppId = await asc.FindAppId(PlayerSettings.applicationIdentifier);
                Persist(d => d.AppId = s.AppId);
            }
            if (string.IsNullOrEmpty(s.AppId))
            {
                reports = new List<CrashReport>();
                progress = "";
                status = "No app record on App Store Connect for "
                    + PlayerSettings.applicationIdentifier
                    + ". Create the app there once, then refresh.";
                return;
            }

            var subs = await asc.ListCrashSubmissions(s.AppId, FetchLimit);
            var built = new List<CrashReport>();
            for (int i = 0; i < subs.Count; i++)
            {
                progress = "Fetching crash " + (i + 1) + " of " + subs.Count + "...";
                Repaint();

                string log = CrashCache.Read(subs[i].Id);
                if (log == null)
                {
                    // One unreadable log must not blank the whole list.
                    try { log = await asc.GetCrashLogText(subs[i].Id); }
                    catch (Exception) { log = null; }
                    if (log != null) CrashCache.Write(subs[i].Id, log);
                }

                var rep = CrashLogParser.Parse(subs[i], log);
                var (cause, hint) = CrashTranslator.Diagnose(rep);
                rep.Cause = cause;
                rep.Hint = hint;
                built.Add(rep);
            }

            reports = built;
            selected = built.Count > 0 ? 0 : -1;
            progress = "";
            status = built.Count == 0
                ? "No TestFlight crashes reported. Apple only collects a crash when "
                  + "the tester taps Share on the crash dialog."
                : "";
            s.CrashCount = built.Count;
            s.CrashSummary = Summarize(built);
            Persist(d => { d.CrashCount = s.CrashCount; d.CrashSummary = s.CrashSummary; });
        }

        public static string Summarize(List<CrashReport> list)
        {
            if (list.Count == 0) return "No TestFlight crashes reported.";
            var newest = list[0];
            string build = string.IsNullOrEmpty(newest.BuildNumber)
                ? "" : " on build " + newest.BuildNumber;
            return list.Count + (list.Count == 1 ? " crash" : " crashes") + build
                + " · newest: " + newest.Cause;
        }

        void OnGUI()
        {
            using (new EditorGUI.DisabledScope(activeTask != null))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    Start(Refresh());
                GUILayout.Label(progress, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            // status carries the deliberately-worded empty-list / no-app-record
            // copy; a toolbar row cannot wrap it, so it renders here instead,
            // where EditorGUILayout can wrap it across as many lines as it needs.
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.Info);

            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
        }

        void DrawList()
        {
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Width(ListWidth));
            for (int i = 0; i < reports.Count; i++)
            {
                var r = reports[i];
                var style = i == selected ? EditorStyles.helpBox : EditorStyles.label;
                EditorGUILayout.BeginVertical(style);
                GUILayout.Label(ShortDate(r.CreatedDate) + "  " + r.DeviceModel,
                    EditorStyles.miniBoldLabel);
                GUILayout.Label("iOS " + r.OsVersion
                    + (string.IsNullOrEmpty(r.BuildNumber) ? "" : "  ·  build " + r.BuildNumber),
                    EditorStyles.miniLabel);
                GUILayout.Label(r.Cause, EditorStyles.wordWrappedMiniLabel);
                // The tester's own words identify a crash faster than any
                // field MacFree derives, so they belong on the row itself.
                if (!string.IsNullOrEmpty(r.Comment))
                    GUILayout.Label("“" + r.Comment + "”",
                        EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();

                var rect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    selected = i;
                    rawFold = false;
                    Event.current.Use();
                    Repaint();
                    // IMGUI replays the control tree built during Layout by
                    // index for every later event in the same pass. DrawDetail's
                    // control count is data-dependent (frame count, which
                    // optional fields are present), so continuing this pass
                    // against the NEW selection throws "Getting control N's
                    // position in a group with only M controls" the moment the
                    // newly selected crash needs a different control count than
                    // the one Layout built. ExitGUI abandons the rest of this
                    // pass; the next Layout pass rebuilds for the new selection.
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawDetail()
        {
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            if (selected < 0 || selected >= reports.Count)
            {
                GUILayout.Label(reports.Count == 0
                    ? "Nothing to show."
                    : "Select a crash on the left.", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                return;
            }

            var r = reports[selected];
            GUILayout.Label(r.Cause, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(r.Hint))
                EditorGUILayout.HelpBox("→ " + r.Hint, MessageType.Info);

            if (!string.IsNullOrEmpty(r.RawLog) && !r.IsSymbolicated)
                EditorGUILayout.HelpBox(
                    "This crash log has no function names — it was not symbolicated. "
                    + "Ship with the GitHub Actions engine, which uploads the debug "
                    + "symbols Apple needs, and future crashes will name your methods.",
                    MessageType.Warning);

            if (!string.IsNullOrEmpty(r.Comment))
            {
                EditorGUILayout.Space();
                GUILayout.Label("Tester said", EditorStyles.boldLabel);
                GUILayout.Label(r.Comment, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
            GUILayout.Label("Device", EditorStyles.boldLabel);
            // appUptimeInMilliseconds / diskBytesAvailable / batteryPercentage
            // are not independently confirmed against Apple's schema, and
            // SimpleJSON returns 0 for a missing key - so a wrong attribute
            // name would otherwise print as a confident, wrong claim ("battery
            // 0%") instead of just omitting the clause a submission left out.
            var deviceLine = new List<string> { r.DeviceModel, "iOS " + r.OsVersion };
            if (r.AppUptimeMs > 0) deviceLine.Add("ran " + (r.AppUptimeMs / 1000) + "s before crashing");
            if (r.BatteryPercentage > 0) deviceLine.Add("battery " + Mathf.RoundToInt((float)r.BatteryPercentage) + "%");
            if (r.DiskBytesAvailable > 0) deviceLine.Add((r.DiskBytesAvailable / 1048576) + " MB free");
            GUILayout.Label(string.Join("  ·  ", deviceLine), EditorStyles.wordWrappedMiniLabel);

            if (!string.IsNullOrEmpty(r.ExceptionType))
            {
                EditorGUILayout.Space();
                GUILayout.Label("Exception", EditorStyles.boldLabel);
                GUILayout.Label(r.ExceptionType
                    + (string.IsNullOrEmpty(r.Signal) ? "" : " (" + r.Signal + ")"),
                    EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(r.ExceptionSubtype))
                    GUILayout.Label(r.ExceptionSubtype, EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(r.TerminationReason))
                    GUILayout.Label(r.TerminationReason, EditorStyles.wordWrappedMiniLabel);
            }

            // Shown ABOVE the crashed thread on purpose: for an uncaught
            // Objective-C exception this is the only block that names the code
            // that threw, while the crashed thread below is abort/unwind
            // machinery the developer did not write and cannot act on.
            if (r.LastExceptionFrames.Count > 0)
            {
                EditorGUILayout.Space();
                GUILayout.Label("Exception backtrace — where it was thrown",
                    EditorStyles.boldLabel);
                foreach (string f in r.LastExceptionFrames)
                    GUILayout.Label(f, EditorStyles.wordWrappedMiniLabel);
            }

            if (r.CrashedThreadFrames.Count > 0)
            {
                EditorGUILayout.Space();
                GUILayout.Label("Crashed thread", EditorStyles.boldLabel);
                foreach (string f in r.CrashedThreadFrames)
                    GUILayout.Label(f, EditorStyles.wordWrappedMiniLabel);
            }

            if (string.IsNullOrEmpty(r.RawLog))
            {
                EditorGUILayout.Space();
                GUILayout.Label("Apple has no log for this submission.", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            rawFold = EditorGUILayout.Foldout(rawFold, "Raw crash log", true);
            if (GUILayout.Button("Copy", GUILayout.Width(50)))
                EditorGUIUtility.systemCopyBuffer = r.RawLog;
            if (GUILayout.Button("Save...", GUILayout.Width(60)))
            {
                string path = EditorUtility.SaveFilePanel("Save crash log",
                    "", r.Id + ".crash", "crash");
                if (!string.IsNullOrEmpty(path))
                {
                    // A failed save (full disk, read-only folder, disconnected
                    // drive) must explain itself in this window, not throw a
                    // raw stack trace into the Editor console.
                    try { System.IO.File.WriteAllText(path, r.RawLog); }
                    catch (Exception e) { error = ErrorTranslator.Translate(e); }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (rawFold)
            {
                // A real .ips is tens to hundreds of KB; SelectableLabel (not
                // TextArea, which is editable and would silently discard
                // typed-in edits on the next repaint) still builds one mesh
                // per element, so the same limit applies - truncate for
                // display only. Copy and Save... above give lossless access
                // to the full log regardless.
                string preview = r.RawLog.Length > RawLogPreviewLimit
                    ? r.RawLog.Substring(0, RawLogPreviewLimit)
                        + "\n... truncated - use Copy or Save... for the full log"
                    : r.RawLog;
                // ExpandHeight made the label claim the whole remaining
                // viewport, so the detail pane's own scrollbar never appeared
                // and everything below the fold was unreachable. Give the log
                // a bounded region with its own scrollbar, and an explicit
                // content height so that scrollbar has something to travel.
                rawScroll = EditorGUILayout.BeginScrollView(
                    rawScroll, GUILayout.Height(RawLogViewHeight));
                float textWidth = Mathf.Max(
                    200f, EditorGUIUtility.currentViewWidth - ListWidth - 60f);
                float textHeight = EditorStyles.label.CalcHeight(
                    new GUIContent(preview), textWidth);
                EditorGUILayout.SelectableLabel(preview, EditorStyles.label,
                    GUILayout.Height(textHeight), GUILayout.Width(textWidth));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndScrollView();
        }

        /// "2026-08-14T10:00:00-07:00" -> "Aug 14, 10:00". The time is not
        /// decoration: several crashes of the same build on the same device
        /// land on one date, and without it every row reads identically and
        /// the newest one cannot be picked out. Falls back to the raw string
        /// if Apple ever changes the format.
        static string ShortDate(string iso)
        {
            DateTime d;
            return DateTime.TryParse(iso, out d) ? d.ToString("MMM d, HH:mm") : iso;
        }
    }
}
