# The Internet PCVR 1.4.3 — Local Data edition

Author: Notsitron. Build version: 1.4.3-localdata.1. Target: Blade & Sorcery PCVR 1.3.2, Windows x64. This has its own source package, separate from the private edition.

**This edition saves website cookies, login state and site data locally.** Its profile is created at `The Internet/BrowserData/Profile` inside the installed mod folder. It does not claim zero data saving or anonymous browsing. The start page, mod name and themed book settings identify it as Local Data.

## Install one edition

Close the game and browser helper. Install only one edition at a time: both use the same game assembly and `The Internet` mod folder. For a new installation, extract the single `The Internet` folder into `BladeAndSorcery_Data/StreamingAssets/Mods`. Do not install the private and local-data DLLs together, and remove the separate runtime folder from an older 1.4.1 installation if present. Players need only the main ZIP, not the optional source ZIP.

No external browser download, installer or account with the mod author is required. The bundled browser program stages/checks itself on a worker at first launch. The game cannot load native browser DLLs as managed scripts, so raw `.dll.payload` files restore their documented normal names in a program-only cache outside Mods. There is one download and one mod folder, without a nested ZIP. See REVIEWER-NOTES.md.

The mod folder must be writable. This edition reports a startup error rather than silently using a temporary profile if BrowserData cannot be created. Browser logins depend on the website: choose its “remember me” option if offered. Expired/revoked credentials, session-only cookies and restrictions on embedded browsers can still require a new login. No separate password-manager feature is added.

## Updates, backups and switching editions

Before replacing this edition, close the game, privately back up `BrowserData`, replace the program files, then restore `BrowserData` into the same mod folder. Keep that backup private; it contains sensitive website/sign-in data. Moving it between PCs or Windows accounts may not preserve usable logins. The updater/build tools do not upload your profile.

To switch to the private edition, first use **Clear saved data** or close the game and remove/privately back up BrowserData. The private edition does not read this folder, but installing it does not securely erase files left by the local-data edition. Never upload an installed mod folder containing BrowserData to Nexus or Git.

## Data controls in the book

Open Book > Mods > The Internet (Local Data). While floating, Browser Settings remains in the book near Return to Book, with the original Blade & Sorcery parchment/ink theme and keyboard.

- **Restart Internet** closes tabs/windows and returns to one home tab while retaining saved cookies and site storage. It waits for the previous helper to exit before reopening the profile.
- **Clear saved data** requires a second selection within six seconds. It docks/merges the browser, closes pages, clears this profile's cookies, site storage, cache and authentication cache, then opens the home page. Sites will be signed out. It is normal browser clearing, not a forensic secure erase; Chromium profile metadata can remain. With the game closed, removing the complete BrowserData folder resets the whole profile.
- **Reset screen size** remains separate. Tabs and panel/control preferences are not automatically restored from prior game sessions; persistent website data is the purpose of this edition.

The settings show saving/clearing failures and clearly state that local data is saved. Browser camera, microphone, location, clipboard, notification, device and restricted filesystem permissions remain denied. Fullscreen and browser persistent-storage requests are permitted. Downloads remain blocked.

## Retained PCVR behavior

The playback/fullscreen fixes are retained. YouTube protection now runs before initial player data is consumed, removes recognized ad slots, hides sponsored placements, and skips/mutes explicitly marked video ads. Updated bundled network/cosmetic filters remain active. One bounded recovery can disable advanced response hooks on a failing tab; network filtering and ad skipping stay enabled. The book shows that state. The blocker toggle reloads pages. YouTube changes frequently, so complete ad blocking or uninterrupted playback cannot be guaranteed.

Ten tabs are supported across floating windows. Tabs can split/rejoin only while floating; docked tabs still open/switch/close. Docking combines tabs without reopening their pages. White capsule grab bars move screens and the head-relative keyboard. Top-left resizing, uncapped world size, scaled cursor reach, upright follow/face tracking, opaque backs, proximity fullscreen exit, dark start/Google pages, fully clickable Nexus/YouTube logo shortcuts, loading-bar toggle and one-click trigger handling remain.

Screen-position audio defaults off, works only while floating, and is disabled on docking. Supported page media uses buffered authenticated loopback PCM, with range scaling by screen size. Unsupported media can retain normal audio. It is not microphone recording or a saved audio file.

## Privacy and publishing

Read PRIVACY.md. BrowserData can contain sensitive sign-in tokens, website data and metadata. Websites receive ordinary requests and information you submit. There is no developer telemetry service or cloud synchronization in the first-party code. Temporary diagnostic files are cleaned after exit where possible; the persistent profile is deliberately retained.

The No Data edition has its own matching build/source archive. Choose it when website state should not be kept across browser processes. For Nexus publication, describe Local Data accurately and provide this matching source. This executable-based internet tool still needs staff review; passing local tests/scans is not approval.

## Build and test

Development requires .NET SDK, Node.js 22.12+ and your own PCVR managed game assemblies. Restore dependencies from a Git checkout with `npm ci --prefix app --ignore-scripts`; the source ZIP includes production dependencies. Run `./build.ps1`, optionally supplying `-GamePath` and `-ElectronArchivePath`. The official Electron 45.3.2 Windows x64 archive is pinned/checksum-verified. Players need none of these development downloads/tools.

Output: `out/pcvr-1.4.3-localdata/The Internet`. `./package.ps1` exports separately named Local Data mod/source ZIPs, a Git-ready source snapshot and checksums. Building/packaging refuses an output folder containing BrowserData. Source export uses a whitelist and Git ignores BrowserData, compiler output, credentials and logs.

Run `node --test app/browser.test.js app/adblock.test.cjs app/youtube-blocker.test.cjs`, `dotnet run --project tests/Clicks.csproj`, and `dotnet run --project tests/RuntimeFilesTests.csproj`. Stage a test runtime with `dotnet run --project tests/RuntimeFilesTests.csproj -- <BrowserRuntime-directory> <test-cache-directory>`, then pass the resulting executable's directory to `node tests/persistence-smoke.cjs <directory>`, `node tests/runtime-smoke.cjs <directory>`, or `node tests/fullscreen-smoke.cjs <directory>`. Fixtures use only generated localhost data and temporary profiles; instrumentation stays out of the player app.

Local checks cover cookie/site-storage retention and clearing across real process restarts, browser isolation/permissions, tab behavior and runtime staging. The new storage controls and writable-profile behavior still need a headset test. No actual account credentials are used in automated tests or shipped in the release/source archives.

YouTube test: `node tests/youtube-smoke.cjs <staged-runtime-directory>` exercises the actual sandboxed preload at a simulated HTTPS YouTube origin, fetch/XHR player data, ad skipping, toggles and bounded recovery. Test-only HTTPS/evaluation instrumentation stays out of the player application. `app/preload.js` is generated from the readable `youtube-blocker.cjs` and `preload-base.js` by `node app/build-preload.cjs` (also run by build.ps1). This is independent protection, not Brave software or an affiliation with Brave.

Release validation: 45 JavaScript/filter checks across both editions passed; the real Electron YouTube-origin fixture passed in each edition. Two muted public YouTube videos played with advancing content time, recognized ad slots removed and no player error or compatibility fallback during the short check. Refresh/fullscreen, ten-tab transfer/merge, runtime verification and generated Local Data persistence/clearing tests passed. These checks do not guarantee every YouTube ad format or replace headset testing.
