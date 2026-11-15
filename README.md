# The Internet — Blade & Sorcery PCVR

Author: **Notsitron**. Source/review candidate: **1.4.3**, targeting Blade & Sorcery PCVR 1.3.2 on Windows x64. Nomad is outside this release.

An in-game web browser with themed book controls, floating screens, up to ten tabs, a VR keyboard, optional positional media audio and bundled ad filtering. Internet access is essential for loading player-selected websites. Publishing this repository does not imply Nexus Mods approval.

## Two separate editions

- [No Data source](no-data/README.md): website sessions are not retained across browser processes. Chromium can create temporary files, and bundled program files remain in the runtime cache. This is not a zero-disk-write guarantee.
- [Local Data source](local-data/README.md): website cookies, logins and site storage are deliberately kept in the installed mod's BrowserData/Profile folder. The book offers Restart Internet and Clear saved data. Tabs/panel preferences are not restored from previous game sessions.

Install only one edition. Neither this repository nor the prepared release ZIPs contain a player profile or real account credentials. Keep BrowserData and personal logs private. Never upload an installed profile.

## Build

Each edition is independently buildable. Read its README and PRIVACY files first. Development requires Node.js 22.12+, .NET SDK and your own installed PCVR game assemblies. Dependencies and the native runtime are development-time downloads, not extra player downloads.

In PowerShell, from either the no-data or local-data directory:

```powershell
npm ci --prefix app --ignore-scripts
./build.ps1 -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Blade & Sorcery'
./package.ps1
```

The official Electron 45.3.2 Windows x64 archive is pinned and SHA-256 verified. For an offline runtime archive, pass -ElectronArchivePath to build.ps1. npm dependencies are pinned by package-lock.json and restored with npm ci. This Git repository omits node_modules; the separate matching source ZIPs include production dependencies and their notices.

## Review entry points

- [No Data reviewer notes](no-data/REVIEWER-NOTES.md) and [privacy](no-data/PRIVACY.md).
- [Local Data reviewer notes](local-data/REVIEWER-NOTES.md) and [privacy](local-data/PRIVACY.md).
- Each edition's src/BrowserEngine.cs launches the helper; src/RuntimeFiles.cs validates/stages bundled program files.
- app/main.js handles the sandboxed offscreen browser, authenticated loopback transport, website permissions and edition-specific storage.
- app/youtube-blocker.cjs and app/preload-base.js are readable sources for the generated sandboxed app/preload.js. No privileged page API is exposed.
- app/cleanup.js removes only validated temporary run folders after the helper exits. Local Data retains BrowserData deliberately.
- app/filter-sources/, app/THIRD-PARTY-NOTICES.md and app/package-lock.json identify filtering resources and dependencies. Keep upstream notices intact. Electron/Chromium notices are also included in the player runtime.

## Executable and DLL layout

TheInternetBrowser.exe is stock Electron electron.exe renamed for the mod. Native browser DLLs are supplied as raw, uncompressed, unencrypted .dll.payload files because the game recursively treats .dll files in Mods as managed scripts. BrowserRuntime/runtime-files.json maps canonical names and hashes. The managed mod stages these program files under the user's local application-data cache before launch. This filename accommodation is documented for inspection; no nested ZIP, installer, executable updater or developer telemetry endpoint is included.

Web pages, input and optional page-media PCM travel over token-authenticated 127.0.0.1 connections. Remote content uses sandboxing, context isolation and web security with Node integration disabled. The mod does not record headset microphones. Websites still receive normal requests and submitted information. Ad blocking is best effort, including on YouTube, and this is not an anonymity tool.

## Matching release files

The matching files are The Internet No Data v1.4.3.zip, The Internet Local Data v1.4.3.zip and their separately named Source ZIPs. [SHA256SUMS.txt](SHA256SUMS.txt) identifies those exact four files. Place binaries/source ZIPs in GitHub Releases rather than committing the native runtime to the code tree. GitHub's automatic repository source archive contains both editions without node_modules; it is different from the separately prepared source ZIP assets.

Validation includes 45 JavaScript/filter checks across both editions, actual sandboxed YouTube-origin fixtures, two short muted public YouTube playback checks, refresh/fullscreen regressions, ten-tab transfers/merging, runtime verification and generated Local Data retention/clearing tests. Local Windows Defender scans found no threats in the four prepared ZIPs. Tests/scans are not a complete audit or Nexus approval. Player headset testing remains necessary.
