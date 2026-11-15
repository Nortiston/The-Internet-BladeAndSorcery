# Privacy — Local Data edition

Version: 1.4.3-localdata.1. Author: Notsitron.

**This edition deliberately saves browsing data on this PC.** It is separate from the private edition. It makes no zero-data-saving, zero-disk-write or anonymity claim.

## What is saved and where

The browser uses a stable persistent Electron partition in `The Internet/BrowserData/Profile` beneath the installed mod folder. Cookies, website authentication state, localStorage, IndexedDB and other Chromium/site profile data can remain across game/browser restarts. Chromium can also retain profile metadata. The whole folder is sensitive and is not an encrypted vault. Do not share it, commit it or upload it as part of a mod ZIP.

Website sign-ins may survive when the website supplies persistent credentials. Session-only cookies, server expiration/revocation, account security checks and embedded-browser restrictions can still prompt login. The mod does not extend cookie expiry, override logout or add a password manager. It does not import another browser's profile. Open tabs/history lists and game UI preferences are not restored by the mod.

First-party code stores no profile on a developer server and includes no developer telemetry endpoint, webhook, cloud synchronization or executable auto-updater. Websites still receive normal requests, the public IP address and information submitted to them. Optional ad blocking is best effort; A failing YouTube tab can retry once with advanced response hooks disabled; network blocking and ad skipping remain active. Ads can still appear. This is not a VPN or anonymity tool.

## Restart and clearing

Restart Internet closes browser windows, flushes saved cookies/storage where possible and starts a fresh home tab using the same profile. It preserves saved website data. Only one helper may use a profile at a time; the mod waits for its previous helper to exit before restarting. Cookie storage is also flushed periodically and after changes. Crashes, forced shutdown, storage failure or website behavior can still lose recent state.

Clear saved data requires two selections, closes pages to stop them rewriting data, clears this browser session's cookies/site storage/cache/authentication and returns home. It does not intentionally touch another mod/browser profile. It is not a secure disk eraser, and profile metadata or recoverable file remnants may remain. For a complete fresh profile, close the game/helper and remove BrowserData manually. Keep backups private if you choose to retain them.

Switching to the private edition does not itself erase an old BrowserData folder or backups. Clear/remove it before switching if the saved information should not remain. To update the local-data edition while keeping logins, privately back it up before replacing program files, then restore it on the same installation. Moving across PCs/accounts may not preserve usable authentication.

## Other files and browser boundaries

Bundled program files stage under `%LOCALAPPDATA%/TheInternet-BAS/runtime/<manifest-hash>`. This separate cache contains code, filters, licenses and icons, not the website profile. Temporary diagnostics use `%TEMP%/TheInternet-BAS/run-<pid>-<timestamp>`. A scoped hidden cleanup process removes only those temporary run directories where possible. It never deletes BrowserData. Program caches and the persistent profile intentionally remain.

Windows paging, crash reporting, antivirus caches, file backups and other software are outside the mod's control. Error messages/logs may include runtime paths; the mod does not upload them. Cleanup and storage persistence are best effort.

Remote content uses Chromium sandboxing, context isolation and web security with Node integration and Electron webviews disabled. Downloads are blocked. Camera/microphone, location, clipboard, notifications, device and restricted filesystem permissions remain denied; fullscreen and persistent-storage permissions are allowed. The isolated preload exposes no general file/shell/game API to websites.

Player head/controller transforms stay local. Optional audio capture is from page media rather than a microphone. Page images/input/state/media PCM travel only over fresh-token-authenticated 127.0.0.1 connections; the mod does not record those streams to disk. Typed/submitted website information can of course be retained by that website and its own storage.

## Source/release privacy

Author metadata is Notsitron. Prepared mod/source archives contain no user profile, account credentials, local integration configuration, logs, debug symbols or Git history. Packaging refuses BrowserData; Git ignores it. Third-party author/copyright/contact credits stay intact. Native Electron provenance and source links are provided in REVIEWER-NOTES.md. Scans/checksums/source review do not prove the absence of vulnerabilities or guarantee Nexus approval.

A public Git profile or commit author/committer can reveal a publisher identity independently of clean source files. Use a fresh repository from this source package and a pseudonymous identity with the hosting account's verified no-reply address if required. Never publish the complete development workspace or an installed profile.
