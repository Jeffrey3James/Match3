using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MacFree.Editor
{
    public class PreflightItem
    {
        public string Label;
        public bool Ok;
        public string FixLabel;
        public Action Fix;
    }

    /// Local ship-blockers with one-click fixes. The icon rules mirror
    /// Apple's ITMS-91111 rejection: a 1024x1024 fully-opaque marketing
    /// icon is mandatory - the single most common first-upload failure.
    public static class Preflight
    {
        public static bool IconIsCompliant(Texture2D tex, out string why)
        {
            if (tex == null) { why = "No app icon assigned."; return false; }
            if (tex.width < 1024 || tex.height < 1024)
            { why = "Icon is " + tex.width + "x" + tex.height + " - Apple requires 1024x1024."; return false; }
            Color32[] px = GetReadablePixels(tex);
            for (int i = 0; i < px.Length; i++)
                if (px[i].a < 255)
                { why = "Icon has transparency - Apple rejects alpha (ITMS-91111)."; return false; }
            why = null;
            return true;
        }

        public static Texture2D FlattenAlpha(Texture2D src)
        {
            Color32[] px = GetReadablePixels(src);
            int sw = src.width, sh = src.height;
            const int S = 1024;
            var outPx = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int sx = Math.Min(sw - 1, x * sw / S);
                    int sy = Math.Min(sh - 1, y * sh / S);
                    Color32 c = px[sy * sw + sx];
                    float a = c.a / 255f;   // composite on white
                    outPx[y * S + x] = new Color32(
                        (byte)(c.r * a + 255 * (1 - a)),
                        (byte)(c.g * a + 255 * (1 - a)),
                        (byte)(c.b * a + 255 * (1 - a)), 255);
                }
            var result = new Texture2D(S, S, TextureFormat.RGBA32, false);
            result.SetPixels32(outPx);
            result.Apply();
            return result;
        }

        /// A compliant stand-in so first uploads never die on ITMS-91111:
        /// 1024x1024, fully opaque, simple two-tone rocket-ish mark. Replace
        /// it with real art any time - it's a normal texture asset.
        public static Texture2D GeneratePlaceholderIcon()
        {
            const int S = 1024;
            var px = new Color32[S * S];
            var top = new Color32(38, 70, 158, 255);     // deep blue
            var bottom = new Color32(24, 160, 152, 255); // teal
            for (int y = 0; y < S; y++)
            {
                float t = y / (float)(S - 1);
                var row = Color32.Lerp(top, bottom, t);
                for (int x = 0; x < S; x++) px[y * S + x] = row;
            }
            // centered light disc + dark core so the icon reads at any size
            DrawDisc(px, S, S / 2, S / 2, 340, new Color32(240, 244, 248, 255));
            DrawDisc(px, S, S / 2, S / 2, 250, new Color32(30, 42, 66, 255));
            DrawDisc(px, S, S / 2, S / 2, 90, new Color32(240, 244, 248, 255));
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static void DrawDisc(Color32[] px, int size, int cx, int cy, int r, Color32 c)
        {
            long r2 = (long)r * r;
            for (int y = Math.Max(0, cy - r); y < Math.Min(size, cy + r); y++)
                for (int x = Math.Max(0, cx - r); x < Math.Min(size, cx + r); x++)
                {
                    long dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2) px[y * size + x] = c;
                }
        }

        /// Write the texture into Assets, import opaque, and assign it as the
        /// default icon AND into every iOS icon slot. The explicit per-slot
        /// assignment matters: Apple's ITMS-91111 validates the 1024
        /// "Marketing" icon in the Xcode asset catalog, and the Unity default
        /// icon alone does not reliably populate it.
        static void AssignIconTexture(Texture2D tex)
        {
            string path = "Assets/MacFreeAppIcon.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = false;
            imp.SaveAndReimport();
            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown,
                new[] { loaded }, IconKind.Any);
            FillIosIconSlots(loaded);
            AssetDatabase.SaveAssets();
        }

        /// Put the texture into every icon slot iOS supports (Application,
        /// Spotlight, Settings, Notification, Marketing). Unity scales each
        /// size at build time. No-op when the iOS module isn't installed.
        static void FillIosIconSlots(Texture2D tex)
        {
            var target = UnityEditor.Build.NamedBuildTarget.iOS;
            var kinds = PlayerSettings.GetSupportedIconKinds(target);
            if (kinds == null) return;
            foreach (var kind in kinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                if (icons == null) continue;
                for (int i = 0; i < icons.Length; i++) icons[i].SetTextures(tex);
                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }
        }

        /// True when every iOS icon slot has a texture. A compliant default
        /// icon still fails Apple if the iOS slots (esp. the 1024 Marketing
        /// icon) are empty. Returns true when it cannot check (no iOS module).
        static bool IosIconSlotsFilled()
        {
            try
            {
                var target = UnityEditor.Build.NamedBuildTarget.iOS;
                var kinds = PlayerSettings.GetSupportedIconKinds(target);
                if (kinds == null || kinds.Length == 0) return true;
                foreach (var kind in kinds)
                {
                    var icons = PlayerSettings.GetPlatformIcons(target, kind);
                    if (icons == null) continue;
                    foreach (var icon in icons)
                    {
                        bool any = false;
                        var texes = icon.GetTextures();
                        if (texes != null)
                            foreach (var t in texes) if (t != null) { any = true; break; }
                        if (!any) return false;
                    }
                }
                return true;
            }
            catch { return true; } // never block shipping on an unreadable API
        }

        /// Icons in projects are often not import-readable; try a direct
        /// GetPixels32() first (works for in-memory/readable textures - this
        /// is what the EditMode tests exercise, headless with -nographics
        /// where RenderTexture.Blit readback is not reliably deterministic).
        /// Fall back to a RenderTexture blit for non-readable imported assets.
        static Color32[] GetReadablePixels(Texture2D tex)
        {
            try
            {
                return tex.GetPixels32();
            }
            // Unity throws UnityException for some non-readable textures and
            // ArgumentException ("texture data is either not readable...") for
            // typical imported icons - both must fall through to the blit.
            catch (UnityException) { }
            catch (ArgumentException) { }

            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy.GetPixels32();
        }

        public static List<PreflightItem> RunLocalChecks(string projectRoot)
        {
            var items = new List<PreflightItem>();

            string bundle = PlayerSettings.applicationIdentifier;
            items.Add(new PreflightItem
            {
                Label = "Bundle ID set (" + bundle + ")",
                Ok = !string.IsNullOrEmpty(bundle)
                     && !bundle.StartsWith("com.DefaultCompany")
                     && bundle.Contains(".")
            });

            Texture2D icon = null;
            var icons = PlayerSettings.GetIcons(
                UnityEditor.Build.NamedBuildTarget.iOS, IconKind.Application);
            if (icons != null && icons.Length > 0) icon = icons[0];
            if (icon == null)
            {
                var any = PlayerSettings.GetIcons(
                    UnityEditor.Build.NamedBuildTarget.Unknown, IconKind.Any);
                if (any != null && any.Length > 0) icon = any[0];
            }
            string why;
            bool iconOk = IconIsCompliant(icon, out why);
            if (iconOk && !IosIconSlotsFilled())
            {
                iconOk = false;
                why = "Icon not applied to the iOS icon slots (App Store 1024).";
            }
            items.Add(new PreflightItem
            {
                Label = iconOk ? "App icon 1024 opaque" : "App icon: " + why,
                Ok = iconOk,
                // No icon at all -> one click generates + assigns a compliant
                // placeholder (Apple hard-rejects iconless uploads, ITMS-91111,
                // after the whole ~30 min build). Existing bad icon -> flatten.
                FixLabel = iconOk ? null : (icon != null ? "Fix icon" : "Add placeholder"),
                Fix = iconOk ? (Action)null : () =>
                {
                    AssignIconTexture(icon != null
                        ? FlattenAlpha(icon) : GeneratePlaceholderIcon());
                }
            });

            string hook = Path.Combine(projectRoot, "ci", "macfree-upload.sh");
            items.Add(new PreflightItem
            {
                Label = "Upload hook written (ci/macfree-upload.sh)",
                Ok = File.Exists(hook),
                FixLabel = "Write hooks",
                Fix = () => ProjectPatcher.WriteHooks(projectRoot)
            });

            bool git = false;
            var dir = new DirectoryInfo(projectRoot);
            while (dir != null && !git)
            { git = Directory.Exists(Path.Combine(dir.FullName, ".git")); dir = dir.Parent; }
            items.Add(new PreflightItem
            {
                Label = "Git repository detected",
                Ok = git
            });

            AddHooksCommittedCheck(items, projectRoot);
            AddUserSettingsIgnoredCheck(items, projectRoot);

            return items;
        }

        static void AddHooksCommittedCheck(List<PreflightItem> items, string projectRoot)
        {
            try
            {
                if (!GitOnPath())
                {
                    items.Add(new PreflightItem
                    {
                        Label = "Hook files committed to git (git not found on PATH)",
                        Ok = false,
                        Fix = null
                    });
                    return;
                }
                string repoRoot = ProjectPatcher.FindRepoRoot(projectRoot) ?? projectRoot;
                string hookPath = Path.Combine(projectRoot, "ci", "macfree-upload.sh");
                bool hookExists = File.Exists(hookPath);
                var (code, stdout, _) = RunGit(repoRoot, "status --porcelain -- ci Assets/MacFree");
                bool clean = code == 0 && string.IsNullOrWhiteSpace(stdout);
                items.Add(new PreflightItem
                {
                    Label = "Hook files committed to git",
                    Ok = clean && hookExists
                    // No button: SET UP commits the hooks automatically
                    // (ProjectPatcher.CommitHooks). This line is informational.
                });
            }
            catch
            {
                items.Add(new PreflightItem
                {
                    Label = "Hook files committed to git (check failed)",
                    Ok = false,
                    Fix = null
                });
            }
        }

        static void AddUserSettingsIgnoredCheck(List<PreflightItem> items, string projectRoot)
        {
            try
            {
                if (!GitOnPath())
                {
                    items.Add(new PreflightItem
                    {
                        Label = "UserSettings ignored by git (git not found on PATH)",
                        Ok = false,
                        Fix = null
                    });
                    return;
                }
                string repoRoot = ProjectPatcher.FindRepoRoot(projectRoot) ?? projectRoot;
                var (code, _, _) = RunGit(repoRoot, "check-ignore UserSettings");
                bool ignored = code == 0;
                items.Add(new PreflightItem
                {
                    Label = "UserSettings ignored by git",
                    Ok = ignored,
                    FixLabel = "Add to .gitignore",
                    Fix = () =>
                    {
                        try
                        {
                            string giPath = Path.Combine(repoRoot, ".gitignore");
                            File.AppendAllText(giPath, "\nUserSettings/\n");
                        }
                        catch { /* best-effort fix; re-check will surface any remaining issue */ }
                    }
                });
            }
            catch
            {
                items.Add(new PreflightItem
                {
                    Label = "UserSettings ignored by git (check failed)",
                    Ok = false,
                    Fix = null
                });
            }
        }

        static bool GitOnPath()
        {
            try
            {
                var (code, _, _) = RunGitIn(Path.GetTempPath(), "--version");
                return code == 0;
            }
            catch { return false; }
        }

        static (int code, string stdout, string stderr) RunGit(string workingDir, string args)
        {
            return RunGitIn(workingDir, args);
        }

        static (int code, string stdout, string stderr) RunGitIn(string workingDir, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, stdout, stderr);
            }
        }
    }
}
