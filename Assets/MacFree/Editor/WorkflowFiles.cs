using System.IO;

namespace MacFree.Editor
{
    /// Generates the two non-secret files MacFree commits for the GitHub
    /// Actions engine: the workflow and the Xcode export options. Everything
    /// sensitive is a repo secret, referenced here by name only.
    public static class WorkflowFiles
    {
        public const string WorkflowPath = ".github/workflows/macfree-ios.yml";
        public const string PlistPath = "ExportOptions.plist";

        /// Two jobs: `build` (Linux) produces the Xcode project as an artifact;
        /// `ship` (macOS, via runnerImage — where Xcode lives) downloads it and
        /// does the archive/export/upload.
        ///
        /// The split is about COST: GitHub bills macOS minutes at 10x, and the
        /// Unity build is the long part (~30-40 min) while the Xcode step is
        /// short. Building on Linux keeps the expensive runner to ~10 minutes.
        /// (It is NOT about licensing: both platforms activate identically,
        /// with UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD. Personal serials
        /// work — game-ci randomizes the machine id for serials starting "F".)
        public static string Yaml(string runnerImage, string unityVersion)
        {
            return
"name: MacFree iOS -> TestFlight\n" +
"on:\n" +
"  workflow_dispatch:\n" +
"  push:\n" +
"    branches: [ main ]\n" +
"concurrency:\n" +
"  group: macfree-ios\n" +
"  cancel-in-progress: false\n" +
"jobs:\n" +
"  build:\n" +
"    runs-on: ubuntu-latest\n" +
"    timeout-minutes: 90\n" +
"    steps:\n" +
"      - uses: actions/checkout@v4\n" +
"      - name: Free disk space\n" +
"        run: |\n" +
"          # game-ci's Unity iOS image (~28 GB unpacked) plus the build overflow\n" +
"          # the ~21 GB free on ubuntu-latest, and Docker dies with \"no space\n" +
"          # left on device\" while unpacking a layer. Delete preinstalled\n" +
"          # toolchains we never use and stale images to reclaim ~30 GB before\n" +
"          # the image is pulled. '|| true' so a missing path never fails setup.\n" +
"          sudo rm -rf /usr/share/dotnet /usr/local/lib/android /opt/ghc \\\n" +
"            /opt/hostedtoolcache/CodeQL /usr/local/share/boost \\\n" +
"            /usr/local/share/powershell /usr/share/swift || true\n" +
"          sudo docker image prune --all --force || true\n" +
"          df -h /\n" +
"      - uses: actions/cache@v4\n" +
"        with:\n" +
"          path: Library\n" +
"          key: Library-${{ hashFiles('Packages/packages-lock.json') }}\n" +
"          restore-keys: Library-\n" +
"      - uses: game-ci/unity-builder@v4\n" +
"        env:\n" +
"          UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}\n" +
"          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}\n" +
"          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}\n" +
"        with:\n" +
"          unityVersion: " + unityVersion + "\n" +
"          targetPlatform: iOS\n" +
"          buildMethod: MacFree.Editor.MacFreeCiBuild.Run\n" +
"          allowDirtyBuild: true\n" +
"      - name: Package Xcode project\n" +
"        run: |\n" +
"          # upload-artifact drops every file's executable bit, but the archive\n" +
"          # later runs Unity's build tools (usymtool, MapFileParser, ...) from\n" +
"          # inside the project. Tarring here carries each file's mode intact\n" +
"          # through the round trip, so nothing is left non-executable on macOS.\n" +
"          tar -cf xcode-project.tar build/iOS\n" +
"      - uses: actions/upload-artifact@v4\n" +
"        with:\n" +
"          name: xcode-project\n" +
"          path: xcode-project.tar\n" +
"          retention-days: 1\n" +
"  ship:\n" +
"    needs: build\n" +
"    runs-on: " + runnerImage + "\n" +
"    timeout-minutes: 90\n" +
"    steps:\n" +
"      - uses: actions/checkout@v4\n" +
"      - name: Select newest Xcode\n" +
"        run: |\n" +
"          # Apple rejects uploads built with an SDK below its current floor\n" +
"          # (e.g. \"must be built with the iOS 26 SDK or later\"), but the runner\n" +
"          # defaults to an older Xcode. Pick the newest Xcode installed, so the\n" +
"          # archive uses the latest SDK and this keeps working as the floor rises.\n" +
"          xc=$(ls -d /Applications/Xcode_*.app 2>/dev/null | sort -V | tail -1)\n" +
"          if [ -z \"$xc\" ]; then echo \"No Xcode found on the runner\" >&2; exit 1; fi\n" +
"          sudo xcode-select -s \"$xc/Contents/Developer\"\n" +
"          xcodebuild -version\n" +
"      - uses: actions/download-artifact@v4\n" +
"        with:\n" +
"          name: xcode-project\n" +
"          path: .\n" +
"      - name: Unpack Xcode project\n" +
"        run: |\n" +
"          # Restores build/iOS with every executable bit intact (see the\n" +
"          # Package step in the build job), so xcodebuild's script phases can\n" +
"          # run Unity's tools instead of dying on \"Permission denied\" minutes\n" +
"          # into the 10x-billed macOS job.\n" +
"          tar -xf xcode-project.tar\n" +
"      - name: Import signing\n" +
"        env:\n" +
"          P12_B64: ${{ secrets.MACFREE_P12_BASE64 }}\n" +
"          P12_PW: ${{ secrets.MACFREE_P12_PASSWORD }}\n" +
"          PROFILE_B64: ${{ secrets.MACFREE_PROFILE_BASE64 }}\n" +
"        run: |\n" +
"          echo \"$P12_B64\" | base64 -d > /tmp/cert.p12\n" +
"          echo \"$PROFILE_B64\" | base64 -d > /tmp/pp.mobileprovision\n" +
"          security create-keychain -p \"\" build.keychain\n" +
"          security default-keychain -s build.keychain\n" +
"          security unlock-keychain -p \"\" build.keychain\n" +
"          # Disable the keychain's default 5-minute auto-lock. The archive\n" +
"          # compiles for several minutes before it codesigns, and a re-locked\n" +
"          # keychain makes codesign hang forever on a GUI unlock prompt that a\n" +
"          # headless runner can never answer (the job just burns to timeout).\n" +
"          security set-keychain-settings build.keychain\n" +
"          security list-keychains -d user -s build.keychain login.keychain\n" +
"          security import /tmp/cert.p12 -k build.keychain -P \"$P12_PW\" -T /usr/bin/codesign -T /usr/bin/xcodebuild\n" +
"          security set-key-partition-list -S apple-tool:,apple: -s -k \"\" build.keychain\n" +
"          mkdir -p \"$HOME/Library/MobileDevice/Provisioning Profiles\"\n" +
"          cp /tmp/pp.mobileprovision \"$HOME/Library/MobileDevice/Provisioning Profiles/\"\n" +
"      - name: Archive and export IPA\n" +
"        env:\n" +
"          TEAM_ID: ${{ secrets.MACFREE_TEAM_ID }}\n" +
"        run: |\n" +
"          XC=$(ls -d build/iOS/Unity-iPhone.xcodeproj build/iOS/*/Unity-iPhone.xcodeproj 2>/dev/null | head -1)\n" +
"          if [ -z \"$XC\" ]; then\n" +
"            echo \"MacFree: could not find Unity-iPhone.xcodeproj under build/iOS (artifact download or Unity build layout changed)\" >&2\n" +
"            exit 1\n" +
"          fi\n" +
"          # The per-target provisioning profile is baked into the Xcode\n" +
"          # project by MacFree's post-process hook (it must go on the app\n" +
"          # target only, never the framework). Here we pass just the team and\n" +
"          # the signing identity, which are valid for every target. Derive the\n" +
"          # identity from the imported cert so its exact name never matters.\n" +
"          IDENTITY=$(security find-identity -v -p codesigning build.keychain | sed -nE 's/.*\"([^\"]+)\".*/\\1/p' | head -1)\n" +
"          xcodebuild -project \"$XC\" -scheme Unity-iPhone -sdk iphoneos -configuration Release -archivePath /tmp/app.xcarchive CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM=\"$TEAM_ID\" CODE_SIGN_IDENTITY=\"$IDENTITY\" archive\n" +
"          xcodebuild -exportArchive -archivePath /tmp/app.xcarchive -exportOptionsPlist ExportOptions.plist -exportPath /tmp/out\n" +
"      - name: Upload to TestFlight\n" +
"        env:\n" +
"          KEY_ID: ${{ secrets.MACFREE_ASC_KEY_ID }}\n" +
"          ISSUER: ${{ secrets.MACFREE_ASC_ISSUER_ID }}\n" +
"          KEY_B64: ${{ secrets.MACFREE_ASC_KEY_BASE64 }}\n" +
"        run: |\n" +
"          mkdir -p ~/private_keys\n" +
"          echo \"$KEY_B64\" | base64 -d > ~/private_keys/AuthKey_${KEY_ID}.p8\n" +
"          xcrun altool --upload-app -f /tmp/out/*.ipa -t ios --apiKey \"$KEY_ID\" --apiIssuer \"$ISSUER\"\n";
        }

        public static string ExportOptionsPlist(string teamId, string bundleId, string profileName)
        {
            return
"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
"<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
"<plist version=\"1.0\">\n<dict>\n" +
"  <key>method</key>\n  <string>app-store</string>\n" +
"  <key>teamID</key>\n  <string>" + teamId + "</string>\n" +
"  <key>signingStyle</key>\n  <string>manual</string>\n" +
"  <key>stripSwiftSymbols</key>\n  <true/>\n" +
"  <key>uploadBitcode</key>\n  <false/>\n" +
"  <key>uploadSymbols</key>\n  <true/>\n" +
"  <key>provisioningProfiles</key>\n  <dict>\n" +
"    <key>" + bundleId + "</key>\n    <string>" + profileName + "</string>\n" +
"  </dict>\n</dict>\n</plist>\n";
        }

        public static void WriteInto(string projectRoot, string runnerImage,
            string unityVersion, string teamId, string bundleId, string profileName)
        {
            string wf = Path.Combine(projectRoot, ".github", "workflows");
            Directory.CreateDirectory(wf);
            File.WriteAllText(Path.Combine(wf, "macfree-ios.yml"), Yaml(runnerImage, unityVersion));
            File.WriteAllText(Path.Combine(projectRoot, "ExportOptions.plist"),
                ExportOptionsPlist(teamId, bundleId, profileName));
        }
    }
}
