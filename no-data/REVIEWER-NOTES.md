# Reviewer notes — The Internet PCVR 1.4.3

Author: Notsitron. Target: Blade & Sorcery PCVR 1.3.2 on Windows x64.

## Executable and one-folder layout

The managed game mod starts an offscreen Electron browser. Internet access is essential for player-selected websites. Page images, input, state and optional media PCM travel over authenticated loopback connections. First-party code includes no developer telemetry endpoint or executable auto-update. Custom application code is plain JavaScript under `BrowserRuntime/resources/app`, with C# source in the separate matching source package.

The game recursively attempts to load every `*.dll` beneath a recognized mod as a managed assembly. Electron's native libraries therefore ship as raw, uncompressed and unencrypted `.dll.payload` files. `BrowserRuntime/runtime-files.json` explicitly maps each payload to its canonical filename and SHA-256. `src/RuntimeFiles.cs` validates paths, rejects traversal/duplicate entries, verifies files, repairs corrupt program caches and stages them atomically outside Mods. `src/BrowserEngine.cs` runs this on a worker before starting the browser. This filename accommodation is for the game's loader; the files retain their normal PE bytes and are available for security inspection.

There is one ZIP and one mod folder. There is no nested Browser.pak ZIP, installer or password protection. Chromium's original `.pak` files are its normal binary resource format. Native bytes are not patched or obfuscated. The staged executable is the stock Electron `electron.exe` renamed `TheInternetBrowser.exe`. All native source/target mappings and hashes are inspectable. Nexus may still require manual review of this layout/executable; no approval is claimed.

## Runtime provenance

Electron 45.3.2, Windows x64:

https://github.com/electron/electron/releases/download/v45.3.2/electron-v45.3.2-win32-x64.zip

Official checksums:

https://github.com/electron/electron/releases/download/v45.3.2/SHASUMS256.txt

Archive SHA-256: `cd1ab0d49c2f76838c0c9ff75afefb4d9d85ca59036691932bf35be8eceba98b`

The build verifies this archive, removes the unused default Electron ASAR/non-English locale packs, adds the visible application and applies the explicit DLL filename mapping. Upstream source: https://github.com/electron/electron/tree/v45.3.2 . No independent audit of the entire browser engine or code-signing certificate is claimed.

Ghostery 2.20.3 and its production dependencies are unmodified packages. First-party wrappers keep network/cosmetic filtering active and skip general YouTube scriptlets. A readable, bounded YouTube-specific function is bundled into the sandboxed preload, serialized synchronously into the page main world through Electron contextBridge.executeInMainWorld, and touches only recognized player ad fields and explicitly marked ads. No Node/IPC API is exposed to pages. A visible player error retries once with response hooks disabled; network filtering, cosmetics and ad skipping stay active. The toggle disables filtering and reloads tabs. No guarantee of all YouTube ads being blocked is made. This does not embed Brave software. Source maps/licenses, filter snapshots and source URLs are supplied. See app/THIRD-PARTY-NOTICES.md and app/package-lock.json.

## Cleanup and privacy

The browser starts a hidden Electron-as-Node instance to run the visible `cleanup.js`. It validates a PID-specific temporary path, waits for that browser to exit and retries removing only its run folder. `process.kill(pid,0)` checks existence rather than terminating a process. C# can terminate only its own spawned browser if graceful shutdown stalls. Program files remain in the cache; browsing session/temp-data limits are documented in PRIVACY.md.

## Reproduce

The source builds without an existing installation of this mod. Install .NET SDK and Node.js 22.12+, obtain PCVR managed game assemblies from your own game installation, restore with `npm ci --prefix app --ignore-scripts` for a Git checkout, then run `./build.ps1 -GamePath <game-directory>`. The source ZIP includes production npm dependencies. Build-time runtime download is pinned/hash-verified and can be supplied offline with `-ElectronArchivePath`. No game DLLs or local developer configuration are distributed.

`build.ps1` produces `out/pcvr-1.4.3/The Internet`; `package.ps1` exports matching mod/source ZIPs, a Git-ready source snapshot and checksums. Compilation is deterministic, without debug symbols and with source paths mapped to `/_src`. README.md lists unit, staging and actual executable tests. Fullscreen test instrumentation runs in a separate temporary developer copy and is absent from the player application. Source review, local antivirus/advisory scans and passing tests are not a Nexus approval or a security guarantee. Provide the exact ZIP/hash, matching source and mod-page link for staff review.
