using System;

namespace MacFree.Editor
{
    /// Turns a raw exception message into one with an actionable next step
    /// appended, based on status code + response body/operation heuristics
    /// observed against the real UBA and ASC APIs. Non-API exceptions pass
    /// through untranslated.
    public static class ErrorTranslator
    {
        public static string Translate(Exception e)
        {
            string original = e.Message;
            var api = e as MacFreeApiException;
            if (api == null) return original;

            string body = api.Body ?? "";
            string bodyLower = body.ToLowerInvariant();
            string opLower = original.ToLowerInvariant();

            string hint = null;

            if (api.Status == 401 && (bodyLower.Contains("permissions") || opLower.Contains("build")))
            {
                hint = "Check your Build Automation API key.";
            }
            else if (api.Status == 401 && (opLower.Contains("certificate") || opLower.Contains("profile")
                || opLower.Contains("bundle") || opLower.Contains("app") || opLower.Contains("crash")))
            {
                hint = "Check the .p8 file, Key ID and Issuer ID.";
            }
            // TestFlight feedback is not readable by every API key role. A bare
            // 403/404 here looks like "the feature is broken" rather than "this
            // key can't see it".
            else if ((api.Status == 403 || api.Status == 404) && opLower.Contains("crash"))
            {
                hint = "This App Store Connect API key can't read TestFlight feedback. "
                    + "It needs the App Manager (or Admin) role — create one at App Store "
                    + "Connect → Users and Access → Integrations, then paste it in step 1.";
            }
            // GitHub blocks pushes touching .github/workflows/ unless the
            // classic token has the 'workflow' scope. MacFree checks the scope
            // up front now, so this mainly catches tokens whose scopes GitHub
            // doesn't report (fine-grained).
            else if (bodyLower.Contains("without `workflow` scope")
                || bodyLower.Contains("refusing to allow a personal access token"))
            {
                hint = "Your GitHub token lacks the 'workflow' scope, which GitHub requires to "
                    + "push the workflow file. Create a new classic token with BOTH 'repo' and "
                    + "'workflow' checked (link in step 1), paste it, and run SET UP again.";
            }
            else if (bodyLower.Contains("provisioning profile is only valid for"))
            {
                // The stored profile was built against a different bundle id.
                // MacFree now invalidates bundle-scoped setup automatically, so
                // this should only reach setups created before that fix.
                hint = "The saved provisioning profile belongs to a different bundle ID. "
                    + "Click \"Reset setup state (advanced)\" and run SET UP again to "
                    + "regenerate the profile for this app's bundle ID.";
            }
            else if (body.Contains("You already have a current") || bodyLower.Contains("reached the limit"))
            {
                hint = "Apple limits Distribution certificates per team. Revoke an unused one at "
                    + "developer.apple.com or reuse the existing.";
            }
            // A bundle-ID "not available" 409 is NOT a harmless duplicate: the
            // identifier is either the Unity template default (which lives under
            // Unity's own namespace) or already taken. Must be checked BEFORE the
            // generic 409 branch below, or the user is wrongly told it is safe.
            else if (bodyLower.Contains("is not available")
                || bodyLower.Contains("attribute.invalid")
                || bodyLower.Contains("enter a different string"))
            {
                hint = "This identifier isn't available on your Apple account — it may still be the "
                    + "Unity template default (com.Unity-Technologies…) or already taken. Set your own "
                    + "unique Bundle Identifier in Player Settings → Identification, then run setup again.";
            }
            // Older targets (or a stale MacFree) can hit UBA's new requirement
            // for a specific Xcode version; MacFree now selects one automatically.
            else if (bodyLower.Contains("xcode version is a required field"))
            {
                hint = "Update MacFree — it now selects a supported Xcode version automatically "
                    + "from Build Automation.";
            }
            // The Unity org hasn't turned on Build Automation. Everything up to
            // the cloud build succeeded; Unity just won't run builds until the
            // org opts in (there is a free tier). Not fixable from the editor.
            else if (bodyLower.Contains("notoptedin") || bodyLower.Contains("billing denied"))
            {
                hint = "Your Unity organization hasn't enabled Build Automation yet. Open the Unity "
                    + "Cloud dashboard → DevOps → Build Automation and activate it for this org "
                    + "(there's a free tier), then run BUILD & SHIP again.";
            }
            // Build Automation clones your project from source control; it has
            // nothing to build until the project lives in a Git repo that is
            // pushed to a remote AND connected to the build target. The
            // Preflight panel flags the same three items (git repo, committed
            // hooks, connected repo) before you ever reach a build.
            else if (bodyLower.Contains("source control is unreachable")
                || bodyLower.Contains("configured source control"))
            {
                hint = "Build Automation builds from your Git repo, and none is connected. Put this "
                    + "project in a Git repository, commit (including MacFree's ci/ hook), and push "
                    + "to GitHub; then connect that repo in the Unity Cloud dashboard → Build "
                    + "Automation → Config → Source Control, and run BUILD & SHIP again.";
            }
            // Duplicate profile names are NOT a harmless duplicate: Apple
            // refuses every later create until they are removed, so setup is
            // stuck. Must precede the generic 409 branch, which would wrongly
            // call it safe. MacFree now clears same-named profiles before
            // creating, so this should only reach clients older than that fix.
            else if (bodyLower.Contains("multiple profiles found"))
            {
                hint = "Apple has duplicate provisioning profiles with the same name and refuses "
                    + "to create more until they're removed. Delete the duplicates at "
                    + "developer.apple.com → Certificates, Identifiers & Profiles → Profiles, "
                    + "then run SET UP again.";
            }
            else if (bodyLower.Contains("already exists") || api.Status == 409)
            {
                hint = "It already exists on App Store Connect — usually safe to continue or reuse.";
            }
            else if (bodyLower.Contains("bundle version must be higher"))
            {
                hint = "App Store Connect requires each upload's build number to exceed the highest "
                    + "ever uploaded. MacFree stamps monotonic numbers — retry the build.";
            }

            return hint == null ? original : original + "\n→ " + hint;
        }

        /// A failed cloud build reports its cause in the build log, not as an
        /// API exception. Scan the log tail for known, actionable causes and
        /// return a "→ ..." hint (or "" when nothing is recognised).
        public static string BuildLogHint(string logTail)
        {
            if (string.IsNullOrEmpty(logTail)) return "";
            string low = logTail.ToLowerInvariant();
            if (low.Contains("failed to populate build attempt with scm")
                || low.Contains("source control is unreachable")
                || low.Contains("configured source control"))
                return "\n→ Build Automation has no repo to build from. Paste a GitHub 'repo' "
                    + "token in step 1, run SET UP (it creates + pushes the repo), then connect "
                    + "it in Build Automation → Settings → Source control and Save.";
            if (low.Contains("exceeded maximum retries for git")
                || low.Contains("checkout failed")
                || (low.Contains("git pat") && low.Contains("error")))
                return "\n→ Build Automation couldn't clone your repo with the GitHub token. "
                    + "In the dashboard (Build Automation → Settings → Source control) the Personal "
                    + "Access Token must be valid and grant Contents: Read to this repository. "
                    + "Regenerate the token with repository access, paste it, Save, then rebuild.";
            if (low.Contains("missing app icon") || low.Contains("91111"))
                return "\n→ Apple rejected the upload: no compliant app icon (ITMS-91111). "
                    + "Use the App icon fix in MacFree's Preflight (or assign a 1024x1024 "
                    + "opaque icon in Player Settings), then BUILD & SHIP again.";
            if (low.Contains("unity license") || low.Contains("license activation")
                || low.Contains("no valid unity")
                || low.Contains("activation strategy could be determined"))
                return "\n→ Unity couldn't activate on the runner. It needs your Unity serial, "
                    + "email AND password (all three) in MacFree step 1 — \"Detect\" fills in the "
                    + "serial this editor uses. Re-run SET UP so the secrets update, then rebuild.";
            // "macos"+"minutes" alone is a near-guaranteed false positive: an
            // xcodebuild log mentions "macosx" SDK paths constantly and
            // "minutes" in ordinary timing lines. Require wording that
            // actually indicates exhaustion, not just co-occurrence.
            bool minutesExhausted = low.Contains("minutes") && (low.Contains("exhausted")
                || low.Contains("out of") || low.Contains("not started"));
            if (low.Contains("runner minutes") || (low.Contains("macos") && minutesExhausted))
                return "\n→ Your GitHub account is out of macOS runner minutes. macOS minutes "
                    + "bill at 10× — add a paid plan or a self-hosted Mac runner, then rebuild.";
            // "provisioning profile" alone appears in virtually every
            // xcodebuild log, including successful ones — require wording
            // that actually indicates a signing failure.
            if (low.Contains("no signing certificate") || low.Contains("code sign error")
                || low.Contains("no profile matching") || low.Contains("failed to code sign"))
                return "\n→ Signing failed on the runner. Re-run SET UP so the certificate, "
                    + "profile, and p12 secrets are regenerated and re-uploaded.";
            return "";
        }
    }
}
