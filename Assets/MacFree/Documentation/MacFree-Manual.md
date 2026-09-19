# MacFree — iOS Builder

Ship iOS builds to TestFlight from Windows or Linux. No Mac, no openssl.

## Prerequisites (one-time, ~10 minutes)
1. Apple Developer Program membership.
2. An App Store Connect API key (App Manager role): App Store Connect →
   Users and Access → Integrations → App Store Connect API → "+". Download
   the .p8 (only downloadable once), note the Key ID and Issuer ID.
3. A Unity account with Build Automation enabled (the free tier works):
   unity.com/products/unity-devops. In the Unity editor, connect your
   project to Unity Cloud (Project Settings → Services).
4. Your project in a git repository, pushed to GitHub/GitLab/Bitbucket.
5. In the Unity Cloud dashboard → Build Automation → Settings → Source
   Control: connect that repository (one-time; Unity has no API for this
   step). 
6. Create your app record once on App Store Connect (Apps → "+") with the
   same bundle ID as your Unity project.

## Every day after that
Open Tools → MacFree iOS Builder:
1. CONNECT — pick the .p8, paste Key ID + Issuer ID + Build Automation API
   key (deep links in the window show where each lives).
2. SET UP EVERYTHING — one click. Signing certificate, provisioning
   profile, build target, upload hook: all created for you. Commit and
   push the generated `ci/macfree-upload.sh` when prompted by the
   preflight list.
3. BUILD & SHIP — one click. ~10–20 minutes later your build is on
   TestFlight.

## Crash reports

Once testers are running your build, **4. CRASHES → Open crash reports** shows
the crashes Apple collected from TestFlight, each with a likely cause and what
to do about it. No SDK is added to your game and nothing is uploaded anywhere —
MacFree reads the crash logs Apple already has, using the same App Store
Connect API key you pasted in step 1.

For each crash you get the device and iOS version, how long the game ran before
it died, the crashed thread with your C# method names, and the raw log.

Two things worth knowing:
- **Apple only collects a crash when the tester taps Share** on the crash
  dialog, so this is a sample of what your testers hit, not a complete record.
- **TestFlight only.** Apple does not publish per-crash stack traces for builds
  that are live on the App Store — only aggregate counts in App Analytics.

If a crash shows addresses instead of method names, that build shipped
without debug symbols. Both engines normally upload them — the GitHub
Actions engine sets `uploadSymbols` itself, and Unity Build Automation has
been verified to symbolicate too — so this is rare. Ship again and later
crashes will name your methods.

One caveat worth knowing: for a crash caused by an uncaught Objective-C
exception (usually a native plugin), the crashed thread is just the system
unwinding the stack and names nothing you wrote. MacFree reads the
**Exception backtrace** section instead, which is where the code that threw
is actually named, and shows it above the crashed thread.

## Troubleshooting
- **"Could not find an operating system" when creating a build target:** update
  the MacFree scripts and run **SET UP EVERYTHING** again. Do not use **Reset
  setup state (advanced)**: completed signing steps are retained, and the failed
  target step is retried. MacFree now checks Unity's macOS compatibility catalog
  using your exact Unity version and each eligible Xcode version, newest first.
  It skips hidden/deprecated entries, beta aliases, and Xcode versions below 26.
  If target provisioning rejects a pair that the catalog accepted, it tries the
  next eligible pair. Other errors stop immediately rather than being hidden.
  If no pair is available, the error names the Unity and Xcode versions checked.
  A newly released editor may not yet be available on Unity's cloud images;
  MacFree cannot install it there or guarantee that changing Xcode will help.
  It never silently downgrades your Unity project.
  Compatibility API: https://build-api.cloud.unity3d.com/docs/#/config
  Xcode availability: https://docs.unity.com/en-us/build-automation/reference/available-xcode-versions
- "bundle version must be higher": Apple remembers the highest build
  number ever uploaded. MacFree stamps monotonic timestamps, so this only
  appears if another tool uploaded a higher number - it resolves itself
  within minutes (the stamp is minutes-since-2026).
- Build succeeds but nothing on TestFlight: open the build log via the
  error panel: the `=== MacFree upload ===` section names the exact
  altool error (missing icon, app record missing, credentials).
- Icon rejected (ITMS-91111): use the preflight "Fix icon" button - Apple
  requires a fully opaque 1024x1024 icon.
- Duplicate BouncyCastle assembly: if your project already references
  BouncyCastle (directly or via another asset/plugin), Unity may report a
  duplicate-assembly conflict for `BouncyCastle.Crypto.dll`. Delete
  whichever copy of the DLL your project isn't actively using (keep the
  one under `Assets/MacFree/Editor/Plugins/` if MacFree is the only
  consumer) and re-import.
