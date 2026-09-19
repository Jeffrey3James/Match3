namespace MacFree.Editor
{
    /// Shared helpers for hand-built JSON request bodies.
    internal static class JsonUtil
    {
        /// Escape a value for embedding inside a hand-built JSON string
        /// literal. Order matters: backslash first, then quote, then the
        /// remaining control characters.
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
