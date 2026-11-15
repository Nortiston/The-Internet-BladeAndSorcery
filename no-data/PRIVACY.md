# Privacy — The Internet PCVR 1.4.3

Author: Notsitron. This describes the PCVR release, not Nomad.

## Browsing and networking

Websites chosen by the player receive ordinary browser requests, the public IP address and information submitted to them. Sites, network providers and other software can retain their own records. This mod is not a VPN or anonymity tool. Ad blocking is optional and best effort. A failing YouTube tab can retry once with advanced response hooks disabled; network blocking and ad skipping remain active. The book settings show that state.

First-party code review found no developer telemetry/analytics endpoint, mod-author login service, webhook, credential/history export, import of other browsers' profiles or executable auto-updater. Runtime binaries, application code, filters and shortcut icons are bundled. Chromium and websites can still perform browser networking; disabling background networking is not a promise that every engine-originated request is eliminated.

Head/controller transforms are used locally for interaction/positioning and are not sent to websites or the author. Optional screen audio captures supported page media, not the headset microphone. Page images, input, state and PCM travel over 127.0.0.1 sockets with fresh per-run tokens. The mod does not record those streams or typed text to files. Typed information submitted on a website is shared with that website.

## Session data, program cache and temporary files

Each browser process uses a nonpersistent Electron partition without a `persist:` prefix and with cache disabled. The mod does not save tabs, browser history, credentials or preferences into a permanent mod database. Cookies/site storage/authentication are shared between tabs for the current browser session and are explicitly cleared at shutdown. Reset Internet closes every browser window and starts a fresh process/session at the local start page. Closing a tab alone does not clear the shared session.

The one-folder installer stages only bundled program files under `%LOCALAPPDATA%/TheInternet-BAS/runtime/<manifest-hash>`. Raw native `.dll.payload` files are copied to their declared normal DLL names, with SHA-256 verification before launch. This cache contains code, licenses, filters and icons, not saved browsing sessions. Older program-cache versions can remain. The staging worker writes no visited URLs or website storage into its manifest or readiness marker.

Chromium can create temporary runtime/profile files under `%TEMP%/TheInternet-BAS/run-<pid>-<timestamp>`. Its user-data, session, crash and log paths point there. HTTP/shader/V8 disk caches, routine logging, spellchecking, component updates and sync are disabled or restricted. A separate hidden cleanup instance waits for browser exit and retries removal of its validated run directory; startup also scavenges stale matching run folders whose process no longer exists.

Cleanup is best effort. Crashes, power loss, file locks, permissions or forced termination can leave temporary files. OS paging, antivirus caches, Windows crash reporting and other software are outside the mod's control. Error logs can contain diagnostic exception details and runtime paths; the mod does not upload them. Do not advertise zero stored data, zero disk writes or guaranteed trace removal.

Suggested wording: **“Browsing sessions and settings are not restored. Browser session data is kept in memory, with best-effort cleanup of temporary Chromium files after exit.”**

## Browser boundaries

Remote pages use Chromium sandboxing, context isolation and web security, with Node integration, insecure active mixed content and Electron webviews disabled. The isolated preload exposes no general filesystem, shell, IPC, credentials or game API to page scripts. It reports editable-field focus, optional page-media PCM/status and a bounded YouTube player-error signal. That error signal contains no page text, account information or URL.

Downloads are blocked. Browser permission checks/requests are denied except fullscreen, including microphone/camera, location, notifications, clipboard and persistent-storage permissions. Device requests and restricted filesystem requests are denied. The mod does not automatically read personal documents. Main navigation/popups are limited to HTTP(S), apart from the bundled home page; local-file/external-application redirects are blocked. The local home-page path is not shown in the normal address display.

A full browser engine still has security risks. Keep the pinned runtime current in future builds. Checksums, antivirus scans and this review do not prove the absence of vulnerabilities in Chromium, dependencies or sites.

## Publisher identity

Published author metadata is Notsitron. Release/source export excludes local compiler response files, debug symbols, logs, integration configuration, credentials and Git history. Third-party copyright/author/contact information stays intact and does not identify the publisher.

Git hosting can reveal real names/emails through the account profile and commit author/committer metadata even if source files are clean. Use a fresh repository from the prepared source, a pseudonymous identity and the hosting provider's own verified no-reply address. Do not publish the whole development workspace or its existing `.git` directory.
