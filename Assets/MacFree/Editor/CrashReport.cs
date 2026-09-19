using System.Collections.Generic;

namespace MacFree.Editor
{
    /// One TestFlight crash submission's metadata, as listed by the App Store
    /// Connect API. The crash log itself is a separate fetch per submission
    /// (AscApi.GetCrashLogText).
    public class CrashSubmission
    {
        public string Id = "";
        public string CreatedDate = "";
        public string DeviceModel = "";
        public string OsVersion = "";
        public long AppUptimeMs;
        public long DiskBytesAvailable;
        public double BatteryPercentage;

        /// What the tester typed when they shared the crash. App Store Connect
        /// shows this in its own column, and it is usually the fastest way to
        /// tell two crashes of the same build apart. Empty when they wrote
        /// nothing, which is common.
        public string Comment = "";
    }

    /// A crash submission plus everything MacFree derived from its log:
    /// the parsed fields (CrashLogParser) and the diagnosis (CrashTranslator).
    public class CrashReport
    {
        // Carried from the submission.
        public string Id = "";
        public string CreatedDate = "";
        public string DeviceModel = "";
        public string OsVersion = "";
        public long AppUptimeMs;
        public long DiskBytesAvailable;
        public double BatteryPercentage;
        public string Comment = "";

        // Parsed out of the log.
        public string BuildNumber = "";
        public string ExceptionType = "";
        public string Signal = "";
        public string ExceptionSubtype = "";
        public string ExceptionCodes = "";
        public string TerminationReason = "";
        public List<string> CrashedThreadFrames = new List<string>();

        /// Apple emits this block only for an uncaught Objective-C exception,
        /// and it is the only place the culprit is named: the crashed thread
        /// of such a crash is pure abort/unwind machinery that says nothing
        /// about which code threw. Empty for every other kind of crash.
        public List<string> LastExceptionFrames = new List<string>();

        /// False when the log's app frames are bare addresses. The UI says so
        /// outright instead of presenting hex as if it were an answer.
        public bool IsSymbolicated;

        /// Null when Apple had no log for this submission.
        public string RawLog;

        // Filled by CrashTranslator.
        public string Cause = "";
        public string Hint = "";
    }
}
