using System.Text.RegularExpressions;

namespace MacFree.Editor
{
    /// IL2CPP compiles every C# method into a C++ function whose name encodes
    /// the class, the method and a uniquing hash — `PlayerController_Update_m1A2B3C`.
    /// Apple symbolicates crash logs with those C++ names, so that is exactly
    /// what a Unity developer is shown. Turn them back into something they
    /// recognise from their own source.
    public static class CrashSymbols
    {
        // The hash is at least four hex/decimal digits; `_gshared` marks the
        // shared implementation of a generic method.
        static readonly Regex Il2Cpp = new Regex(
            @"^(?<body>[A-Za-z_][A-Za-z0-9_]*?)_m[0-9A-Fa-f]{4,}(_gshared)?$",
            RegexOptions.Compiled);

        /// `PlayerController_Update_m1A2B3C` -> `PlayerController.Update`.
        /// Anything that is not an IL2CPP symbol comes back untouched.
        public static string Demangle(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return symbol;
            var m = Il2Cpp.Match(symbol);
            if (!m.Success) return symbol;

            string body = m.Groups["body"].Value;
            int cut = body.LastIndexOf('_');
            // No separator means a free function, not Class_Method — splitting
            // it would produce a leading or trailing dot.
            if (cut <= 0 || cut == body.Length - 1) return body;
            return body.Substring(0, cut) + "." + body.Substring(cut + 1);
        }
    }
}
