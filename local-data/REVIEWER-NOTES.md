# Reviewer notes — PCVR Local Data edition

Author: Notsitron. Version: 1.4.3-localdata.1. Target: Blade & Sorcery PCVR 1.3.2, Windows x64. This source package is independent of the private edition.

## Differences that matter for review

This edition persists website data under the installed mod's `BrowserData/Profile` folder. The managed launcher passes that explicit path; the browser uses it for user/session data with the persistent `persist:the-internet-local` partition. Closing/restarting flushes cookies and DOM storage rather than deleting them. The separate Clear saved data control closes pages and explicitly clears cookies/storage/cache/authentication. Headings, start-page disclosures, manifest and documentation identify Local Data; it is not described as zero-saving/private browsing.

Only temporary diagnostic directories are passed to cleanup.js. The persistent profile is outside that validated cleanup namespace. C# serializes restart behind the previous helper's exit, and the browser requests a profile instance lock. Startup fails if its required BrowserData path is missing/not writable; it does not silently select a profile outside the mod folder. Packaging refuses BrowserData and source export is whitelisted. No real account/profile is part of this distribution or its tests.

## Executable and one-folder packaging

The managed mod starts bundled Electron for offscreen HTML rendering. Internet access is essential for sites selected by the player. Images, input, state and optional page-media PCM use authenticated 127.0.0.1 connections. Remote content has sandboxing/context isolation/web security on and Node integration off. The local-data edition permits fullscreen and persistent-storage permissions; other browser permissions and downloads remain denied. No developer telemetry, executable updater or cloud profile service is included.

The game recursively loads `*.dll` under a mod as managed scripts. Native browser DLLs therefore ship as raw `.dll.payload` files with explicit canonical filenames/SHA-256 values in `BrowserRuntime/runtime-files.json`. `src/RuntimeFiles.cs` validates paths, verifies bytes and atomically stages the program outside Mods. The files are uncompressed/unencrypted and retain inspectable PE bytes. There is no nested ZIP, password protection, installer or modified native executable. This loader accommodation still needs explaining to Nexus staff; approval is not claimed.

## Provenance

Native runtime: Electron 45.3.2 Windows x64, with electron.exe renamed TheInternetBrowser.exe and DLL filename mapping only. The build verifies the official archive hash before extraction, removes the unused default ASAR and non-English locales, and adds visible application JS. Original executable/DLL bytes are unchanged.

Archive: https://github.com/electron/electron/releases/download/v45.3.2/electron-v45.3.2-win32-x64.zip

Checksum list: https://github.com/electron/electron/releases/download/v45.3.2/SHASUMS256.txt

SHA-256: `cd1ab0d49c2f76838c0c9ff75afefb4d9d85ca59036691932bf35be8eceba98b`

Upstream source: https://github.com/electron/electron/tree/v45.3.2 . A complete independent audit of Chromium/Electron or publisher code-signing certificate is not claimed.

Ghostery 2.20.3 and its production dependencies are unmodified packages. First-party wrappers keep network/cosmetic filtering active and skip general YouTube scriptlets. A readable, bounded YouTube-specific function is bundled into the sandboxed preload, serialized synchronously into the page main world through Electron contextBridge.executeInMainWorld, and touches only recognized player ad fields and explicitly marked ads. No Node/IPC API is exposed to pages. A visible player error retries once with response hooks disabled; network filtering, cosmetics and ad skipping stay active. The toggle disables filtering and reloads tabs. No guarantee of all YouTube ads being blocked is made. This does not embed Brave software. Source maps/licenses, filter snapshots and source URLs are supplied. See app/THIRD-PARTY-NOTICES.md and app/package-lock.json.

## Reproduce

Install .NET SDK and Node.js 22.12+, obtain game managed assemblies from your own PCVR installation, restore dependencies for a Git checkout with `npm ci --prefix app --ignore-scripts`, then run `./build.ps1 -GamePath <game-directory>`. The source ZIP includes production dependencies. The build fetches only the pinned/hash-verified development-time runtime archive, or accepts it through `-ElectronArchivePath`. Players require no external downloads/tools.

Output: `out/pcvr-1.4.3-localdata/The Internet`. `package.ps1` emits separately named Local Data build/source ZIPs, a source snapshot and versioned checksums. Compilation is deterministic, without debug symbols and with source paths mapped to `/_src`. README.md lists generated-localhost persistence, tab, fullscreen and staging tests. Instrumentation exists only in isolated developer test copies and is absent from the player app. No game DLLs, personal account data or developer integration configuration are distributed.

Nexus review should receive the exact main-file hash, matching Local Data source and mod-page link. The private edition has a separate source archive and different storage behavior. Antivirus/advisory scans and automated tests do not replace staff review or establish that every website is safe.
