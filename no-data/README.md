# The Internet PCVR 1.4.3 — No Data edition

Author: Notsitron. Target: Blade & Sorcery PCVR 1.3.2, Windows x64. Nomad is deferred.

## Install

Install only one edition at a time; both use the same DLL and mod folder. Close the game. Replace the old `The Internet` mod folder with the single **The Internet** folder from this ZIP, under `BladeAndSorcery_Data/StreamingAssets/Mods`. Remove the separate `The Internet Browser Runtime` folder from a previous 1.4.1 installation if present; the new version contains its own runtime. Do not merge old files into this folder. There is one player download and one mod folder. The separate source ZIP is optional for developers/reviewers.

No installer, separate browser download, account registration or extra player tools are needed. First launch shows a preparation/checking message while a worker verifies and stages the bundled program files. This can take several seconds. The game's recursive managed DLL loader cannot safely load native browser libraries, so those libraries are supplied as raw `.dll.payload` files with an explicit checksum/filename manifest. The mod restores their canonical names in a program-only cache outside Mods. The files are uncompressed, unencrypted and directly inspectable; there is no nested ZIP. See REVIEWER-NOTES.md.

## This update

- YouTube protection runs at document start: it removes recognized player ad slots, hides sponsored placements, and skips/mutes explicitly identified video ads while preserving normal content. Updated bundled network/cosmetic filters remain active. A visible playback error triggers at most one retry with advanced response hooks disabled for that tab; network filtering and ad skipping stay enabled. Settings disclose compatibility mode. The blocker toggle reloads all tabs. YouTube can change, and complete blocking or uninterrupted playback cannot be guaranteed.
- Refresh exits stale fullscreen state, keeps viewport/zoom synchronized and queues a fullscreen request made during navigation until the new document is ready. Late old resize commands and mismatched image sizes are rejected. Renderer crash recovery is limited to prevent endless reload loops.
- **Reset Internet** is in the themed book settings. It closes browser tabs/windows, clears the current browser session by restarting its process, and returns to one start tab docked in the book. Ad blocking returns to its default on state and screen audio returns off. Panel placement/reach preferences remain for the current mod session; **Reset screen size** is a separate control.
- Tabs can split/rejoin only in pop-out mode. Docked tabs still open, switch and close normally. Dragging a tab from the book cannot create a world window; both the gesture handler and backend enforce this.
- Blocker toggling keeps one stable session integration instead of repeatedly removing/re-registering IPC handlers during page loads. Failed background injections are caught. Local home-page paths are hidden from the address display.

## Controls and retained features

Open Book > Mods > The Internet. Pop Out creates a floating screen. While floating, **Browser Settings** appears in the book beside **Return to Book**. Settings and keyboard keep the Blade & Sorcery parchment/ink theme; browser/start pages are dark. Google home/search pages have dark styling. Nexus Mods and YouTube shortcuts use full clickable logo buttons.

Up to ten tabs are shared across floating windows. Drag a tab outside a floating tab strip to split it; drop it on another floating tab strip to rejoin it. Docking combines all tabs into the book while preserving their actual pages. The final tab returns to the start page. Use book arrows to select a floating window's settings.

Each screen and keyboard has a thin white capsule underneath for movement. Hold the trigger there to move it; use the thumbstick to push/pull. The keyboard opens in front of the player and moves independently. Resize from the proximity-revealed top-left corner; there is no maximum world-size cap and a small minimum prevents a degenerate panel. Cursor reach grows with screen width. Follow/face-player tracking uses a level horizon and avoids head roll. Opaque backs hide reversed page images.

Fullscreen hides browser chrome and has a proximity-revealed exit. Single-click mode defaults on and emits one click per trigger release; turn it off for hold/drag behavior. Keyboard backspace/arrow repeat remains intentional. Settings include cursor distance, reset screen size, hide loading bar, follow/face position controls, ad blocking and optional screen audio. Cursor color options remain removed.

Screen-position audio defaults off and is available only while floating. Docking disables it. Supported page media streams through an independent authenticated loopback connection into buffered Unity audio; range scales with screen width. Unsupported cross-origin/site-owned audio graphs can keep normal sound. Audio is not recorded to a file. Headset listening and arbitrary website behavior still need player testing.

## Privacy

Sessions/settings are not restored. Browser session data stays in memory, and temporary Chromium files are cleaned after exit where possible. Websites receive normal requests and information you submit. This is not an anonymity tool or a zero-disk-write guarantee. Program files remain in the runtime cache. No developer telemetry endpoint, executable auto-update or browsing-history export is included. Read PRIVACY.md for the precise scope and limits.

## Build and checks

Development requires .NET SDK, Node.js 22.12+ and your own installed PCVR game assemblies. Restore with `npm ci --prefix app --ignore-scripts` for a fresh Git checkout; runtime dependencies and filters are included in the source ZIP. Run `./build.ps1`, optionally specifying `-GamePath` and `-ElectronArchivePath`. The build verifies the pinned official Electron 45.3.2 Windows x64 archive before use. Players do not need these tools or downloads.

Output is `out/pcvr-1.4.3/The Internet`. `./package.ps1` exports the main/source ZIPs, Git-ready source snapshot and checksums. Generated caches, compiler files and Git metadata are excluded. Explicit filter refresh is a development-only `node app/build-filters.cjs` command; the running mod uses bundled filters.

Checks: `node --test app/browser.test.js app/adblock.test.cjs app/youtube-blocker.test.cjs`, `dotnet run --project tests/Clicks.csproj`, and `dotnet run --project tests/RuntimeFilesTests.csproj`. To stage a built runtime for standalone executable tests, run `dotnet run --project tests/RuntimeFilesTests.csproj -- <BrowserRuntime-directory> <test-cache-directory>`; its last output line is the staged executable. Pass that executable's directory to `node tests/runtime-smoke.cjs <directory>` and `node tests/fullscreen-smoke.cjs <directory>`. Fullscreen instrumentation exists only in an isolated developer test copy and is absent from the release app.

Validation includes JavaScript/filter checks, actual sandboxed document-start YouTube-origin fixtures, runtime staging, tab/fullscreen regressions and storage checks. The YouTube fixture uses generated player responses and explicitly marked fake ads; it does not prove coverage of all live YouTube ad formats. Headset testing is still required. Passing local tests/scans is not Nexus approval or proof of security.

YouTube test: `node tests/youtube-smoke.cjs <staged-runtime-directory>` exercises the actual sandboxed preload at a simulated HTTPS YouTube origin, fetch/XHR player data, ad skipping, toggles and bounded recovery. Test-only HTTPS/evaluation instrumentation stays out of the player application. `app/preload.js` is generated from the readable `youtube-blocker.cjs` and `preload-base.js` by `node app/build-preload.cjs` (also run by build.ps1). This is independent protection, not Brave software or an affiliation with Brave.

Release validation: 45 JavaScript/filter checks across both editions passed; the real Electron YouTube-origin fixture passed in each edition. Two muted public YouTube videos played with advancing content time, recognized ad slots removed and no player error or compatibility fallback during the short check. Refresh/fullscreen, ten-tab transfer/merge, runtime verification and generated Local Data persistence/clearing tests passed. These checks do not guarantee every YouTube ad format or replace headset testing.
