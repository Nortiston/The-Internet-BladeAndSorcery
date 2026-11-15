'use strict';
// Exercise the real engine entrypoint with fake Electron/socket APIs, without launching the game.
const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const { EventEmitter } = require('node:events');

async function engine() {
  const sent = [], loaded = [], sizes = [], cleared = [], switches = [], paths = [];
  const blockerActions = [];
  let reloads = 0;
  const rendererMessages = [];
  const insertedCss = [];
  let requestPermission, checkPermission, windowOptions, popup;
  const ses = new EventEmitter();
  Object.assign(ses, {
    setPermissionRequestHandler: cb => requestPermission = cb,
    setPermissionCheckHandler: cb => checkPermission = cb,
    setSpellCheckerEnabled() {},
    clearStorageData: async () => cleared.push('storage'),
    clearCache: async () => cleared.push('cache'),
    clearAuthCache: async () => cleared.push('auth')
  });
  ses.flushStorageData = () => cleared.push('flush-storage');
  ses.cookies = new EventEmitter(); ses.cookies.flushStore = async () => cleared.push('flush-cookies');
  const windows = [];
  function contents() {
    const wc = new EventEmitter();
    let url = '', muted = false;
    Object.assign(wc, { session: ses, getURL: () => url, getTitle: () => 'Test',
    canGoBack: () => true, canGoForward: () => false, isLoading: () => false,
    isAudioMuted: () => muted, setAudioMuted: v => muted = v,
    getUserAgent: () => 'Test Electron/1.0', setUserAgent() {},
    setFrameRate() {}, focus() {}, setWindowOpenHandler: cb => wc.popup = cb, reload: () => reloads++, send: (...args) => rendererMessages.push(args),
    executeJavaScript: async () => null,
    insertCSS: async (...args) => { insertedCss.push(args); return 'style'; },
    loadURL: async u => { url = u; loaded.push(u); }, sendInputEvent: e => sent.push(e)
    });
    return wc;
  }
  const socket = new EventEmitter(); socket.write = () => true; socket.destroy = () => { socket.destroyed = true; };
  const app = new EventEmitter();
  Object.assign(app, { setPath: (...p) => paths.push(p), commandLine: { appendSwitch: (...s) => switches.push(s) },
    whenReady: () => Promise.resolve(), quit: () => cleared.push('quit'), exit() {} });
  let partition;
  const electron = { app, nativeTheme: {}, ipcMain: new EventEmitter(), session: {
    fromPartition: (p, options) => { partition = { p, options }; return ses; }
  }, BrowserWindow: class {
    constructor(options) { windowOptions = options; this.webContents = contents(); windows.push(this); this.destroyed = false; }
    setContentSize(w, h) { sizes.push([w, h]); }
    destroy() { this.destroyed = true; }
  }};
  const fakeProcess = new EventEmitter();
  Object.assign(fakeProcess, { argv: ['engine', '--port=1234', '--token=test','--data-dir='+path.join(require('node:os').tmpdir(),'TheInternet-local-unit','BrowserData')], pid: 999,
    kill() {} });
  const context = vm.createContext({ Buffer, Promise, URL, console, __dirname, process: fakeProcess,
    setTimeout() {}, setInterval() {}, require: name => {
      if (name === 'electron') return electron;
      if (name === 'net') return { createConnection: () => socket };
      if (name === 'child_process') return { spawn: () => ({ on() {}, unref() {} }) };
      if (name === '@ghostery/adblocker-electron') return { ElectronBlocker: { deserialize: () => ({ onBeforeRequest:(_details,callback)=>callback({cancel:true}), enableBlockingInSession: s => { assert.equal(s, ses); blockerActions.push('enable'); }, disableBlockingInSession: s => { assert.equal(s, ses); blockerActions.push('disable'); } }) } };
      if (name === 'fs') return { existsSync: () => false, mkdirSync() {}, rmSync() {}, readFileSync: () => Buffer.alloc(0) };
      return require(name);
    }
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, 'main.js'), 'utf8'), context);
  await new Promise(resolve => setImmediate(resolve));
  const command = obj => {
    const payload = Buffer.from(JSON.stringify(obj));
    const header = Buffer.alloc(5); header[0] = 1; header.writeUInt32LE(payload.length, 1);
    socket.emit('data', Buffer.concat([header, payload]));
  };
  const activeContents = () => vm.runInContext('win.webContents', context);
  return { context, command, ses, ipc:electron.ipcMain, socket, get wc() { return activeContents(); }, windows, loaded, sizes, sent, partition, windowOptions, switches, paths, cleared, blockerActions, rendererMessages, insertedCss, reloads: () => reloads,
    requestPermission: p => new Promise(resolve => requestPermission(activeContents(), p, resolve)),
    checkPermission: p => checkPermission(activeContents(), p), popup: url => activeContents().popup({ url }) };
}

test('ad blocker toggle updates the local-data session and reloads once per change', async () => {
  const e = await engine();
  assert.deepEqual(e.blockerActions, ['enable']);
  e.command({ t: 'adblock', v: false });
  assert.equal(vm.runInContext('adBlockEnabled', e.context), false);
  assert.deepEqual(e.blockerActions, ['enable']);
  assert.equal(e.reloads(), 1);
  e.command({ t: 'adblock', v: false });
  assert.equal(e.reloads(), 1);
  e.command({ t: 'adblock', v: true });
  assert.equal(vm.runInContext('adBlockEnabled', e.context), true);
  assert.deepEqual(e.blockerActions, ['enable']);
  assert.equal(e.reloads(), 2);
});

test('screen audio defaults off and is reapplied to a newly loaded page', async () => {
  const e = await engine();
  assert.equal(vm.runInContext('spatialAudioEnabled', e.context), false);
  e.command({ t: 'spatialAudio', v: true });
  assert.equal(vm.runInContext('spatialAudioEnabled', e.context), false);
  e.command({ t: 'floating', v: true });
  e.command({ t: 'spatialAudio', v: true });
  assert.equal(vm.runInContext('spatialAudioEnabled', e.context), true);
  assert.deepEqual(e.rendererMessages.at(-1), ['ti-spatial-audio', true]);
  e.wc.emit('did-finish-load');
  assert.deepEqual(e.rendererMessages.at(-1), ['ti-spatial-audio', true]);
  e.command({ t: 'spatialAudio', v: false });
  assert.deepEqual(e.rendererMessages.at(-1), ['ti-spatial-audio', false]);
});

test('docking disables screen audio, refuses docked enables and stays off after pop-out', async () => {
  const e = await engine();
  e.command({t:'floating',v:true}); e.command({t:'spatialAudio',v:true});
  assert.equal(vm.runInContext('spatialAudioEnabled',e.context),true);
  e.command({t:'floating',v:false});
  assert.equal(vm.runInContext('spatialAudioEnabled',e.context),false);
  assert.deepEqual(e.rendererMessages.at(-1), ['ti-spatial-audio',false]);
  e.command({t:'spatialAudio',v:true});
  assert.equal(vm.runInContext('spatialAudioEnabled',e.context),false);
  e.wc.emit('did-finish-load');
  assert.deepEqual(e.rendererMessages.at(-1), ['ti-spatial-audio',false]);
  e.command({t:'floating',v:true});
  assert.equal(vm.runInContext('spatialAudioEnabled',e.context),false);
});

test('ten-tab limit, independent navigation, active audio and close/switch behavior', async () => {
  const e = await engine();
  e.command({ t:'nav', url:'https://example.com/first' });
  for (let i=0;i<12;i++) e.command({t:'newTab'});
  assert.equal(e.windows.length,10);
  assert.equal(vm.runInContext('tabs.length',e.context),10);
  assert.equal(vm.runInContext('activeTab',e.context),9);
  assert.ok(e.windows.slice(0,9).every(w=>w.webContents.isAudioMuted()));
  e.command({t:'selectTab',index:0});
  assert.equal(e.wc.getURL(),'https://example.com/first');
  assert.equal(e.wc.isAudioMuted(),false);
  assert.equal(e.windows[9].webContents.isAudioMuted(),true);
  e.command({t:'closeTab',index:0});
  assert.equal(e.windows[0].destroyed,true);
  assert.equal(vm.runInContext('tabs.length',e.context),9);
  for(let i=0;i<12;i++) e.command({t:'closeTab',index:0});
  assert.equal(vm.runInContext('tabs.length',e.context),1);
  assert.match(e.wc.getURL(),/start\.html$/);
  e.command({t:'newTab'});
  assert.equal(vm.runInContext('tabs.length',e.context),2);
});

test('detaching/moving tabs preserves their page objects; docking merges all windows',async()=>{
  const e=await engine();e.command({t:'floating',v:true});e.command({t:'newTab'});e.command({t:'nav',url:'https://example.com/moving'});
  const page=e.wc,loads=e.loaded.length;
  e.command({t:'detachTab',index:1,position:{x:1,y:2,z:3}});
  assert.equal(vm.runInContext('groups.size',e.context),2);
  assert.equal(vm.runInContext('groups.get(1).tabs[0].win.webContents',e.context),page);
  assert.equal(e.loaded.length,loads);
  e.command({t:'moveTab',windowId:1,index:0,targetWindowId:0});
  assert.equal(vm.runInContext('groups.size',e.context),1);assert.equal(e.wc,page);assert.equal(e.loaded.length,loads);
  e.command({t:'detachTab',index:1});e.command({t:'newTab',windowId:2});
  const selected=vm.runInContext('current(groups.get(2)).win.webContents',e.context);
  e.command({t:'mergeWindows',preferredWindowId:2});
  assert.equal(vm.runInContext('groups.size',e.context),1);assert.equal(vm.runInContext('tabs.length',e.context),3);
  assert.equal(e.wc,selected);assert.equal(vm.runInContext('groups.get(0).floating',e.context),false);
});

test('audio packets queue through a busy fallback video socket instead of being discarded',async()=>{
  const e=await engine();e.command({t:'floating',v:true});e.command({t:'spatialAudio',v:true});
  vm.runInContext('canWrite=false',e.context);
  e.ipc.emit('ti-audio',{sender:e.wc},new Float32Array(4096));
  assert.equal(vm.runInContext('audioQueue.length',e.context),1);
  e.socket.emit('drain');assert.equal(vm.runInContext('audioQueue.length',e.context),0);
});

test('Google home/search dark styling is scoped away from other websites', async () => {
  const e = await engine();
  e.command({t:'nav',url:'https://www.google.com/search?q=test'}); e.wc.emit('dom-ready');
  e.command({t:'nav',url:'https://www.google.com/'}); e.wc.emit('dom-ready');
  assert.equal(e.insertedCss.length,2);
  e.command({t:'nav',url:'https://www.youtube.com/'}); e.wc.emit('dom-ready');
  e.command({t:'nav',url:'https://www.google.com.example.com/search'}); e.wc.emit('dom-ready');
  assert.equal(e.insertedCss.length,2);
});

test('start page, home, search, navigation and popup handling', async () => {
  const e = await engine();
  const home = e.loaded[0];
  assert.match(home, /\/start\.html$/);
  e.command({ t: 'nav', url: 'youtube.com' });
  assert.equal(e.loaded.at(-1), 'https://youtube.com');
  e.command({ t: 'nav', url: 'blade and sorcery' });
  assert.equal(e.loaded.at(-1), 'https://www.google.com/search?q=blade%20and%20sorcery');
  e.command({ t: 'home' }); assert.equal(e.loaded.at(-1), home);
  e.popup('https://www.nexusmods.com/bladeandsorcery');
  assert.equal(e.loaded.at(-1), 'https://www.nexusmods.com/bladeandsorcery');
  const count = e.loaded.length;
  e.popup('file:///private.txt'); assert.equal(e.loaded.length, count);
  let prevented = false;
  e.wc.emit('will-navigate', { preventDefault: () => prevented = true }, 'file:///private.txt');
  assert.ok(prevented);
});

test('fullscreen state reaches game; viewport resizes and Escape is forwarded', async () => {
  const e = await engine();
  e.wc.emit('enter-html-full-screen');
  assert.equal(vm.runInContext('htmlFullscreen', e.context), true);
  e.command({ t: 'resize', w: 1280, h: 858, fullscreen:true });
  assert.deepEqual(e.sizes.at(-1), [1280, 858]);
  e.command({ t: 'key', key: 'Escape' });
  assert.equal(e.sent.at(-1).keyCode, 'Escape');
  e.wc.emit('leave-html-full-screen');
  assert.equal(vm.runInContext('htmlFullscreen', e.context), false);
  e.wc.emit('enter-html-full-screen');
  e.wc.emit('did-start-navigation', {}, 'https://example.com', false, true);
  assert.equal(vm.runInContext('htmlFullscreen', e.context), false);
  const previousSizes = e.sizes.length;
  e.command({ t: 'resize', w: 99999, h: 99999 });
  assert.equal(e.sizes.length, previousSizes);
});

test('persistent local profile, restricted permissions and flush without erasing logins', async () => {
  const e = await engine();
  assert.equal(e.partition.p,'persist:the-internet-local');
  assert.equal(e.partition.options.cache, false);
  assert.equal(e.windowOptions.webPreferences.sandbox, true);
  assert.equal(e.windowOptions.webPreferences.nodeIntegration, false);
  assert.equal(e.windowOptions.webPreferences.contextIsolation, true);
  assert.equal(e.windowOptions.webPreferences.webSecurity, true);
  assert.equal(e.windowOptions.webPreferences.webviewTag, false);
  assert.equal(e.windowOptions.webPreferences.allowRunningInsecureContent, false);
  assert.equal(e.windowOptions.webPreferences.disableHtmlFullscreenWindowResize, true);
  assert.ok(e.switches.some(([name]) => name === 'disable-http-cache'));
  for (const p of ['media', 'geolocation', 'notifications', 'clipboard-read', 'clipboard-sanitized-write', 'fileSystem', 'hid', 'usb', 'serial']) {
    assert.equal(await e.requestPermission(p), false);
    assert.equal(e.checkPermission(p), false);
  }
  assert.equal(await e.requestPermission('fullscreen'), true);
  assert.equal(await e.requestPermission('persistent-storage'), true);
  let blocked = false;
  e.ses.emit('will-download', { preventDefault: () => blocked = true });
  assert.ok(blocked);
  blocked = false;
  e.ses.emit('file-system-access-restricted', { preventDefault: () => blocked = true });
  assert.ok(blocked);
  assert.deepEqual(e.paths.map(p => p[0]), ['userData','sessionData','crashDumps','logs']);
  for (const [name, p] of e.paths) assert.match(p, name==='userData'||name==='sessionData'?/BrowserData[\\/]Profile$/:/TheInternet-BAS[\\/]run-999-/);
  e.command({ t: 'quit' });
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(e.cleared, ['flush-storage','flush-cookies','quit']);
});

test('redirects to local files and embedded webviews are blocked', async () => {
  const e = await engine();
  for (const url of ['file:///private.txt','steam://run/123','javascript:alert(1)','data:text/html,test']) {
    let prevented=false;e.wc.emit('will-redirect',{preventDefault:()=>prevented=true},url);assert.equal(prevented,true);
  }
  let prevented=false;e.wc.emit('will-redirect',{preventDefault:()=>prevented=true},'https://example.com/');assert.equal(prevented,false);
  e.wc.emit('will-attach-webview',{preventDefault:()=>prevented=true});assert.equal(prevented,true);
});

test('oversized or non-command packets close the local command connection', async () => {
  for (const [type,length] of [[1,65537],[2,8]]) {
    const e = await engine(), header=Buffer.alloc(5);header[0]=type;header.writeUInt32LE(length,1);e.socket.emit('data',header);assert.equal(e.socket.destroyed,true);
  }
});

test('docked tabs cannot split or accept a dragged tab, while floating tabs can',async()=>{
  const e=await engine();e.command({t:'newTab'});e.command({t:'detachTab',index:0});
  assert.equal(vm.runInContext('groups.size',e.context),1);
  e.command({t:'floating',v:true});e.command({t:'detachTab',index:0});assert.equal(vm.runInContext('groups.size',e.context),2);
  e.command({t:'floating',windowId:0,v:false});e.command({t:'moveTab',windowId:1,index:0,targetWindowId:0});
  assert.equal(vm.runInContext('groups.size',e.context),2);
  e.command({t:'mergeWindows'});assert.equal(vm.runInContext('groups.size',e.context),1);
});

test('stale fullscreen resize is refused after navigation resets the viewport',async()=>{
  const e=await engine();e.wc.emit('enter-html-full-screen');assert.deepEqual(e.sizes.at(-1),[1280,858]);
  e.wc.emit('did-start-navigation',{},'https://example.com/',false,true);
  assert.equal(vm.runInContext('htmlFullscreen',e.context),false);assert.deepEqual(e.sizes.at(-1),[1280,720]);
  e.command({t:'resize',w:1280,h:858,fullscreen:true});assert.deepEqual(e.sizes.at(-1),[1280,720]);
  e.wc.emit('enter-html-full-screen');assert.equal(vm.runInContext('current(groups.get(0)).pendingFullscreen',e.context),true);
  assert.equal(vm.runInContext('htmlFullscreen',e.context),false);
});

test('YouTube recovery retries once without bypassing network blocking',async()=>{
  const e=await engine();e.command({t:'nav',url:'https://www.youtube.com/watch?v=test'});
  e.ipc.emit('ti-playback-error',{sender:e.wc});assert.equal(vm.runInContext('current(groups.get(0)).youtubeRelaxed',e.context),true);
  assert.equal(vm.runInContext('current(groups.get(0)).recoveryAttempts',e.context),1);
  const configEvent={sender:e.wc};e.ipc.emit('ti-youtube-config',configEvent);assert.equal(configEvent.returnValue.enabled,true);assert.equal(configEvent.returnValue.advanced,false);
  let result;vm.runInContext('blocker',e.context).onBeforeRequest({webContentsId:e.wc.id},v=>result=v);assert.equal(result.cancel,true);
  assert.match(vm.runInContext('current(groups.get(0)).playbackStatus',e.context),/network blocking stays on/);
  e.ipc.emit('ti-playback-error',{sender:e.wc});assert.equal(vm.runInContext('current(groups.get(0)).recoveryAttempts',e.context),1);
  assert.match(vm.runInContext('current(groups.get(0)).playbackStatus',e.context),/Restart Internet/);
  e.command({t:'adblock',v:false});assert.equal(vm.runInContext('current(groups.get(0)).youtubeRelaxed',e.context),false);
});

test('clear saved data closes old pages, clears site data and returns to one home tab',async()=>{
  const e=await engine();e.command({t:'newTab'});const old=e.windows.slice();e.command({t:'clearData'});
  await new Promise(resolve=>setImmediate(resolve));
  assert.ok(old.every(w=>w.destroyed));assert.equal(vm.runInContext('totalTabs()',e.context),1);
  for(const type of ['storage','cache','auth','flush-storage','flush-cookies'])assert.ok(e.cleared.includes(type));
  assert.equal(vm.runInContext('clearingData',e.context),false);assert.match(e.loaded.at(-1),/start\.html$/);
});
