// The Internet - browser engine for the Blade & Sorcery mod.
// Runs Chromium off-screen (no window), streams page images to the game over a
// local-only socket (127.0.0.1) and receives mouse/keyboard input back.
// Local Data edition: cookies/site storage persist in the mod's BrowserData folder.
// Only temporary diagnostics are cleaned after exit. See PRIVACY.md.
'use strict';
const { app, BrowserWindow, ipcMain, session, nativeTheme } = require('electron');
const { ElectronBlocker } = require('@ghostery/adblocker-electron');
const net = require('net');
const os = require('os');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const { spawn } = require('child_process');

// ---------- arguments ----------
function arg(name, def) {
  const p = '--' + name + '=';
  const a = process.argv.find(x => x.startsWith(p));
  return a ? a.substring(p.length) : def;
}
const PORT = parseInt(arg('port', '0'), 10);
const AUDIO_PORT = parseInt(arg('audio-port', '0'), 10);
const TOKEN = arg('token', '');
const PARENT = parseInt(arg('parent', '0'), 10);
let WIDTH = parseInt(arg('width', '1280'), 10);
let HEIGHT = parseInt(arg('height', '720'), 10);
const HOME = pathToFileURL(path.join(__dirname, 'start.html')).href;
const FPS = parseInt(arg('fps', '30'), 10);
const FULLSCREEN_HEIGHT = parseInt(arg('fullscreen-height', String(HEIGHT + 138)), 10);
const DATA_DIR = arg('data-dir', '');

if (!PORT || !TOKEN) { app.exit(1); }
if (!DATA_DIR || !path.isAbsolute(DATA_DIR) || path.basename(DATA_DIR) !== 'BrowserData') { app.exit(3); throw new Error('Local Data edition requires its absolute BrowserData directory.'); }
const PROFILE_DIR = path.join(DATA_DIR, 'Profile');
try { fs.mkdirSync(PROFILE_DIR, {recursive:true}); }
catch { app.exit(3); throw new Error('Cannot write the mod BrowserData folder.'); }

// ---------- persistent profile + separate throwaway diagnostics ----------
const TEMP_ROOT = path.join(os.tmpdir(), 'TheInternet-BAS');
const RUN_DIR = path.join(TEMP_ROOT, 'run-' + process.pid + '-' + Date.now());
try {
  // remove leftovers from a previous crash
  if (fs.existsSync(TEMP_ROOT)) {
    for (const d of fs.readdirSync(TEMP_ROOT)) {
      const match = /^run-(\d+)-\d+$/.exec(d);
      if (!match) continue;
      // Do not wipe a second running instance's temporary profile.
      try { process.kill(Number(match[1]), 0); continue; } catch (e) { if (e.code !== 'ESRCH') continue; }
      try { fs.rmSync(path.join(TEMP_ROOT, d), { recursive: true, force: true }); } catch (e) { }
    }
  }
  fs.mkdirSync(RUN_DIR, { recursive: true });
} catch (e) { app.exit(1); throw new Error('Cannot prepare the temporary browser profile; refusing to start.'); }
app.setPath('userData', PROFILE_DIR);
app.setPath('sessionData', PROFILE_DIR);
app.setPath('crashDumps', path.join(RUN_DIR, 'crash'));
app.setPath('logs', path.join(RUN_DIR, 'logs'));
if (app.requestSingleInstanceLock && !app.requestSingleInstanceLock()) { app.exit(2); }
function wipe() { try { fs.rmSync(RUN_DIR, { recursive: true, force: true }); } catch (e) { } }
// Chromium locks its profile until after will-quit/exit. A separate, hidden Node
// watcher waits for this process to die and retries removal after locks release.
const cleaner = spawn(process.execPath, [path.join(__dirname, 'cleanup.js'), RUN_DIR, String(process.pid)], {
  env: { ...process.env, ELECTRON_RUN_AS_NODE: '1' }, detached: true, windowsHide: true, stdio: 'ignore'
});
cleaner.on('error', () => { }); // next startup also scavenges stale run directories
cleaner.unref();

app.commandLine.appendSwitch('force-device-scale-factor', '1');
app.commandLine.appendSwitch('lang', 'en-US'); // only the English language pack is bundled
app.commandLine.appendSwitch('disable-gpu-shader-disk-cache');
app.commandLine.appendSwitch('disable-http-cache');
app.commandLine.appendSwitch('disable-application-cache');
app.commandLine.appendSwitch('disable-breakpad');
app.commandLine.appendSwitch('disable-logging');
app.commandLine.appendSwitch('disable-background-networking');
app.commandLine.appendSwitch('disable-component-update');
app.commandLine.appendSwitch('disable-sync');
app.commandLine.appendSwitch('disk-cache-size', '1');
app.commandLine.appendSwitch('autoplay-policy', 'no-user-gesture-required');
app.commandLine.appendSwitch('disable-features', 'HardwareMediaKeyHandling,MediaSessionService');
app.disableDomainBlockingFor3DAPIs && app.disableDomainBlockingFor3DAPIs();

// ---------- socket protocol ----------
// frame: [type:1 byte][length:4 bytes LE][payload]
//   to game: 1 = JSON, 2 = image (width, height, windowId, BGRA pixels)
//   audio pipe: authenticated hello, then 3 = rate, channels, windowId, tabId, float32 PCM
//   from game: 1 = JSON command
let sock = null;
let win = null;
const pendingFrames = new Map();
let canWrite = true;
let htmlFullscreen = false;
let blocker = null;
let adBlockEnabled = true;
let spatialAudioEnabled = false;
let isFloating = false;
let audioRoutingStatus = 'Normal browser audio.';
const tabs = [];
let activeTab = -1;
let browserSession = null;
let clearingData = false;
let dataStatus = 'Local Data: saved in BrowserData. Restart keeps logins; Clear saved data signs you out.';
let nextTabId = 1;
let audioSock = null;
let audioCanWrite = true;
const audioQueue = [];

function writePacket(socket, type, payload) {
  const head = Buffer.alloc(5);
  head.writeUInt8(type, 0); head.writeUInt32LE(payload.length, 1);
  socket.write(head);
  return socket.write(payload);
}

function sendRaw(type, payload) {
  if (!sock || sock.destroyed) return false;
  return writePacket(sock, type, payload);
}
function sendEvent(obj) { sendRaw(1, Buffer.from(JSON.stringify(obj), 'utf8')); }

function flushAudio() {
  const target = audioSock || sock;
  if (!target || target.destroyed || (audioSock ? !audioCanWrite : !canWrite)) return;
  while (audioQueue.length) {
    const ready = writePacket(target, 3, audioQueue.shift().payload);
    if (audioSock) audioCanWrite = ready; else canWrite = ready;
    if (!ready) return;
  }
}

function clearQueuedAudio(windowId) {
  if(windowId===undefined){audioQueue.length=0;return;}
  for(let i=audioQueue.length-1;i>=0;i--)if(audioQueue[i].windowId===windowId)audioQueue.splice(i,1);
}

function connectAudio() {
  if (!AUDIO_PORT) return; // compatibility for standalone protocol tests
  audioSock = net.createConnection({ host: '127.0.0.1', port: AUDIO_PORT }, () => audioSock.setNoDelay(true));
  writePacket(audioSock, 1, Buffer.from(JSON.stringify({t:'hello',token:TOKEN}), 'utf8'));
  audioSock.on('drain', () => { audioCanWrite = true; flushAudio(); });
  const restoreNormalAudio = () => {
    clearQueuedAudio();
    if (quitting) return;
    for(const g of groups.values()){
      g.spatial=false;g.audioStatus='Audio connection lost; normal audio restored.';
      const tab=current(g);if(tab)tab.win.webContents.send('ti-spatial-audio',false);state(g);
    }
  };
  audioSock.on('error', restoreNormalAudio);
  audioSock.on('close', restoreNormalAudio);
}

function sendFrame(image, windowId = 0) {
  const size = image.getSize();
  const bmp = image.toBitmap(); // BGRA, top-down
  const head = Buffer.alloc(12);
  head.writeInt32LE(size.width, 0);
  head.writeInt32LE(size.height, 4);
  head.writeInt32LE(windowId, 8);
  pendingFrames.set(windowId, Buffer.concat([head, bmp]));
  flushFrame();
}
function flushFrame() {
  while (pendingFrames.size && canWrite) {
    const [id, frame] = pendingFrames.entries().next().value;
    pendingFrames.delete(id);
    canWrite = sendRaw(2, frame);
  }
}

const groups = new Map();
let nextWindowId = 1;
function totalTabs(){let n=0;for(const g of groups.values())n+=g.tabs.length;return n;}
function current(g){return g.tabs[g.active];}
function youtube(url){try{const h=new URL(url).hostname;return h==='youtube.com'||h.endsWith('.youtube.com')||h==='youtube-nocookie.com'||h.endsWith('.youtube-nocookie.com');}catch{return false;}}
function applyViewport(tab){
  if(!tab||tab.closed||quitting)return;
  const g=tab.group,wc=tab.win.webContents;if(current(g)!==tab)return;
  tab.win.setContentSize(g.width,g.height);
  wc.setZoomFactor&&wc.setZoomFactor(1);
  wc.invalidate&&wc.invalidate();
}
function settleViewport(tab){
  applyViewport(tab);
  for(const delay of [80,240])setTimeout(()=>applyViewport(tab),delay);
}
function leaveFullscreen(tab){
  if(!tab||tab.closed)return;
  tab.fullscreen=false;
  if(tab.win.isFullScreen&&tab.win.isFullScreen())tab.win.setFullScreen(false);
  tab.win.webContents.executeJavaScript('if(document.fullscreenElement)document.exitFullscreen().catch(()=>{})').catch(()=>{});
  if(current(tab.group)===tab){tab.group.width=WIDTH;tab.group.height=HEIGHT;pendingFrames.delete(tab.group.id);settleViewport(tab);}
}
function reloadTab(tab,explicit=true){
  if(!tab||tab.closed)return;
  if(explicit)tab.recoveryAttempts=0;
  tab.pendingFullscreen=false;tab.navigating=true;leaveFullscreen(tab);
  clearQueuedAudio(tab.group.id);tab.win.webContents.reload();state(tab.group);
}
function recoverPlayback(tab){
  if(!tab||tab.closed||!youtube(tab.win.webContents.getURL()))return;
  if(tab.recoveryAttempts>=1){tab.playbackStatus='YouTube still cannot play. Try Reload or Restart Internet.';state(tab.group);return;}
  tab.recoveryAttempts++;
  tab.youtubeRelaxed=adBlockEnabled;
  tab.playbackStatus=tab.youtubeRelaxed?'YouTube retry: hooks off; network blocking stays on.':'Retrying YouTube playback once.';
  state(tab.group);
  setTimeout(()=>{if(!tab.closed&&!quitting)reloadTab(tab,false);},400);
}
function queuedFullscreen(tab,attempt=0){
  if(!tab||tab.closed||!tab.pendingFullscreen||tab.navigating||quitting)return;
  tab.win.webContents.executeJavaScript(`(()=>{const e=document.querySelector('#movie_player')||document.querySelector('video');if(!e)return false;if(document.fullscreenElement)return true;return e.requestFullscreen().then(()=>true,()=>false)})()`,true).then(ok=>{
    if(ok)tab.pendingFullscreen=false;
    else if(attempt<4)setTimeout(()=>queuedFullscreen(tab,attempt+1),300);
    else tab.pendingFullscreen=false;
  }).catch(()=>{tab.pendingFullscreen=false;});
}
function installCompatibleBlocker(){
  const request=blocker.onBeforeRequest;
  if(request)blocker.onBeforeRequest=(details,callback)=>{
    if(!adBlockEnabled)return callback({});
    return request(details,callback);
  };
  const inject=blocker.onInjectCosmeticFilters;
  if(inject)blocker.onInjectCosmeticFilters=(event,url,msg)=>{
    if(!adBlockEnabled)return;
    if(!youtube(url))return inject(event,url,msg);
    const tab=findTab(event.sender);if(!tab||tab.youtubeCssDocument===tab.documentGeneration)return;
    tab.youtubeCssDocument=tab.documentGeneration;
    // Keep cosmetic CSS; old YouTube scriptlets can interfere with its changing player.
    try{const f=blocker.getCosmeticsFilters({domain:'youtube.com',hostname:new URL(url).hostname,url,getBaseRules:true,getInjectionRules:false,getRulesFromHostname:true});if(f.styles)event.sender.insertCSS(f.styles,{cssOrigin:'user'}).catch(()=>{});}catch{}
  };
  const headers=blocker.onHeadersReceived;
  if(headers)blocker.onHeadersReceived=(details,callback)=>adBlockEnabled?headers(details,callback):callback({});
  const observe=blocker.onIsMutationObserverEnabled;
  if(observe)blocker.onIsMutationObserverEnabled=event=>adBlockEnabled?observe(event):false;
}
function state(g=groups.get(0)){
  if(!g||!groups.has(g.id))return;
  const tab=current(g),wc=tab&&tab.win.webContents;
  if(g.id===0){win=tab?tab.win:null;activeTab=g.active;htmlFullscreen=!!(tab&&tab.fullscreen);spatialAudioEnabled=g.spatial;isFloating=g.floating;audioRoutingStatus=g.audioStatus;}
  sendEvent({t:'state',windowId:g.id,windowPosition:g.position,empty:!wc,url:wc&&wc.getURL()!==HOME?wc.getURL():'',title:wc?wc.getTitle():'No tabs',
    canBack:wc?wc.navigationHistory?wc.navigationHistory.canGoBack():wc.canGoBack():false,
    canFwd:wc?wc.navigationHistory?wc.navigationHistory.canGoForward():wc.canGoForward():false,
    loading:wc?wc.isLoading():false,muted:wc?wc.isAudioMuted():false,fullscreen:!!(tab&&tab.fullscreen),adBlockEnabled,playbackStatus:tab&&tab.playbackStatus||'',youtubeCompatibility:!!(tab&&tab.youtubeRelaxed),localDataEnabled:true,clearingData,dataStatus,
    spatialAudioEnabled:g.spatial,isFloating:g.floating,audioRoutingStatus:g.audioStatus,audioBackend:g.audioBackend||'',activeTab:g.active,
    activeTabId:tab?tab.id:0,totalTabs:totalTabs(),tabs:g.tabs.map(t=>({title:t.win.webContents.getTitle()||'New Tab',url:t.win.webContents.getURL()}))});
}
function broadcast(){for(const g of groups.values())state(g);}
function rates(){const count=Math.max(1,[...groups.values()].filter(g=>g.tabs.length).length);for(const g of groups.values())for(const t of g.tabs)t.win.webContents.setFrameRate(Math.min(FPS,Math.max(3,Math.floor(60/count))));}
function makeGroup(id,position){const g={id,position,tabs:id===0?tabs:[],active:0,floating:id!==0,spatial:false,audioStatus:'Normal browser audio.',width:WIDTH,height:HEIGHT};groups.set(id,g);return g;}
function toUrl(text){text=(text||'').trim();if(!text)return HOME;if(/^https?:\/\//i.test(text))return text;if(!/\s/.test(text)&&/^[^\s]+\.[a-z]{2,}(:\d+)?(\/.*)?$/i.test(text))return 'https://'+text;return 'https://www.google.com/search?q='+encodeURIComponent(text);}
const KEYMAP={Backspace:'Backspace',Enter:'Return',Tab:'Tab',Delete:'Delete',Escape:'Escape',Left:'Left',Right:'Right',Up:'Up',Down:'Down',Home:'Home',End:'End',Space:'Space'};
function deactivate(tab){if(!tab)return;const wc=tab.win.webContents;tab.fullscreen=false;wc.send('ti-spatial-audio',false);wc.setAudioMuted(true);wc.stopPainting&&wc.stopPainting();wc.executeJavaScript('if(document.fullscreenElement)document.exitFullscreen()').catch(()=>{});}
function activateTab(index,g=groups.get(0)){
  if(!g||index<0||index>=g.tabs.length)return;
  if(g.active!==index)deactivate(current(g));
  g.active=index;const tab=current(g),wc=tab.win.webContents;
  g.height=tab.fullscreen?FULLSCREEN_HEIGHT:HEIGHT;
  clearQueuedAudio(g.id);settleViewport(tab);
  wc.setAudioMuted(tab.muted);wc.send('ti-spatial-audio',g.spatial);wc.startPainting&&wc.startPainting();wc.invalidate&&wc.invalidate();wc.focus();
  sendEvent({t:'focus',windowId:g.id,editable:false});state(g);
}
function emptied(g){if(g.tabs.length){g.active=Math.min(g.active,g.tabs.length-1);activateTab(g.active,g);}else if(g.id===0){g.active=0;state(g);}else{groups.delete(g.id);sendEvent({t:'windowClosed',windowId:g.id});}}
function closeTab(index,g=groups.get(0)){
  if(!g||index<0||index>=g.tabs.length)return;
  if(totalTabs()===1){current(g).win.webContents.loadURL(HOME).catch(()=>{});return;}
  const tab=g.tabs[index];deactivate(tab);g.tabs.splice(index,1);if(index<g.active)g.active--;tab.closed=true;emptied(g);tab.win.destroy();rates();broadcast();
}
function moveTab(source,index,target){
  if(!source||!target||!source.floating||!target.floating||source===target||index<0||index>=source.tabs.length)return;
  const tab=source.tabs[index];deactivate(tab);source.tabs.splice(index,1);if(index<source.active)source.active--;
  tab.group=target;target.tabs.push(tab);emptied(source);activateTab(target.tabs.length-1,target);rates();broadcast();
}
function detachTab(g,index,position){
  if(!g||!g.floating||index<0||index>=g.tabs.length)return;
  if(g.tabs.length===1){sendEvent({t:'moveWindow',windowId:g.id,position});return;}
  const target=makeGroup(nextWindowId++,position);moveTab(g,index,target);
}
function mergeWindows(preferred){
  const root=groups.get(0),from=groups.get(preferred)||root,selected=current(from);
  for(const g of [...groups.values()]){
    deactivate(current(g));g.spatial=false;clearQueuedAudio(g.id);
    if(g.id===0)continue;
    for(const tab of g.tabs){tab.group=root;root.tabs.push(tab);}g.tabs.length=0;groups.delete(g.id);sendEvent({t:'windowClosed',windowId:g.id});
  }
  root.floating=false;root.spatial=false;root.audioStatus='Normal browser audio (docked).';
  activateTab(Math.max(0,root.tabs.indexOf(selected)),root);rates();broadcast();
}
function handle(cmd){
  if(clearingData&&cmd.t!=='quit')return;
  const g=groups.get(cmd.windowId|0);if(!g&&cmd.t!=='quit')return;
  const tab=g&&current(g),wc=tab&&tab.win.webContents;
  switch(cmd.t){
    case 'hello':break;
    case 'newTab':createTab(HOME,g);break;
    case 'selectTab':activateTab(cmd.index|0,g);break;
    case 'closeTab':closeTab(cmd.index|0,g);break;
    case 'detachTab':detachTab(g,cmd.index|0,cmd.position);break;
    case 'moveTab':moveTab(g,cmd.index|0,groups.get(cmd.targetWindowId|0));break;
    case 'mergeWindows':mergeWindows(cmd.preferredWindowId|0);break;
    case 'nav':if(wc)wc.loadURL(toUrl(cmd.url)).catch(()=>{});break;
    case 'home':if(wc)wc.loadURL(HOME).catch(()=>{});break;
    case 'back':if(wc){if(wc.navigationHistory)wc.navigationHistory.goBack();else wc.goBack();}break;
    case 'fwd':if(wc){if(wc.navigationHistory)wc.navigationHistory.goForward();else wc.goForward();}break;
    case 'reload':if(wc)reloadTab(tab);break;
    case 'stop':if(wc)wc.stop();break;
    case 'mute':if(wc){tab.muted=!!cmd.v;wc.setAudioMuted(tab.muted);state(g);}break;
    case 'adblock':{
      const enabled=!!cmd.v;if(blocker&&enabled!==adBlockEnabled){adBlockEnabled=enabled;for(const group of groups.values())for(const t of group.tabs){t.youtubeRelaxed=false;t.playbackStatus='';t.win.webContents.send('ti-youtube-config',youtubeConfig(t));reloadTab(t);}}broadcast();break;
    }
    case 'floating':g.floating=!!cmd.v;if(!g.floating){g.spatial=false;clearQueuedAudio(g.id);g.audioStatus='Normal browser audio (docked).';if(wc)wc.send('ti-spatial-audio',false);}state(g);break;
    case 'spatialAudio':g.spatial=!!cmd.v&&g.floating;if(!g.spatial)clearQueuedAudio(g.id);g.audioStatus=g.spatial?'Waiting for supported page media.':'Normal browser audio.';if(wc)wc.send('ti-spatial-audio',g.spatial);state(g);break;
    case 'mouse':if(wc){wc.focus();const e={type:cmd.kind,x:Math.round(cmd.x),y:Math.round(cmd.y)};if(cmd.kind!=='mouseMove'){e.button=cmd.button||'left';e.clickCount=cmd.clicks||1;}wc.sendInputEvent(e);}break;
    case 'wheel':if(wc)wc.sendInputEvent({type:'mouseWheel',x:Math.round(cmd.x),y:Math.round(cmd.y),deltaX:cmd.dx||0,deltaY:cmd.dy||0,canScroll:true});break;
    case 'text':if(wc){wc.focus();for(const ch of cmd.s||'')wc.sendInputEvent({type:'char',keyCode:ch});}break;
    case 'key':if(wc){wc.focus();const k=KEYMAP[cmd.key]||cmd.key,mods=cmd.shift?['shift']:[];if(k==='Escape')tab.pendingFullscreen=false;wc.sendInputEvent({type:'keyDown',keyCode:k,modifiers:mods});if(k==='Return')wc.sendInputEvent({type:'char',keyCode:'\r',modifiers:mods});wc.sendInputEvent({type:'keyUp',keyCode:k,modifiers:mods});}break;
    case 'resize':{const w=cmd.w|0,h=cmd.h|0;if(w>64&&h>64&&w<=4096&&h<=4096&&tab){const expected=tab.fullscreen?FULLSCREEN_HEIGHT:HEIGHT;if((typeof cmd.fullscreen==='boolean'&&cmd.fullscreen!==tab.fullscreen)||h!==expected){state(g);break;}g.width=w;g.height=h;pendingFrames.delete(g.id);settleViewport(tab);}}break;
    case 'quit':shutdown();break;
    case 'clearData':clearSavedData();break;
  }
}
function connect(){
  sock=net.createConnection({host:'127.0.0.1',port:PORT},()=>sock.setNoDelay(true));sendEvent({t:'hello',token:TOKEN});
  sock.on('drain',()=>{canWrite=true;flushAudio();flushFrame();});let buffer=Buffer.alloc(0);
  sock.on('data',data=>{buffer=Buffer.concat([buffer,data]);while(buffer.length>=5){const type=buffer[0],length=buffer.readUInt32LE(1);if(type!==1||length>65536){sock.destroy();return;}if(buffer.length<5+length)break;const payload=buffer.subarray(5,5+length);buffer=buffer.subarray(5+length);try{handle(JSON.parse(payload.toString('utf8')));}catch{}}});
  sock.on('close',shutdown);sock.on('error',shutdown);
}
let quitting=false;
function flushSavedData(){
  if(!browserSession)return Promise.resolve();
  try{browserSession.flushStorageData();return browserSession.cookies.flushStore().catch(error=>{dataStatus='Cannot save browser data. Check BrowserData permissions and free disk space.';broadcast();throw error;});}
  catch(error){dataStatus='Cannot save browser data. Check BrowserData permissions and free disk space.';broadcast();return Promise.reject(error);}
}
async function clearSavedData(){
  if(clearingData||!browserSession)return;
  clearingData=true;clearQueuedAudio();pendingFrames.clear();
  dataStatus='Clearing saved cookies and site data...';
  const root=groups.get(0);
  for(const g of [...groups.values()]){
    g.spatial=false;
    for(const tab of g.tabs){tab.closed=true;tab.win.destroy();}
    g.tabs.length=0;g.active=0;
    if(g.id!==0){groups.delete(g.id);sendEvent({t:'windowClosed',windowId:g.id});}
  }
  broadcast();
  try{
    await Promise.all([browserSession.clearStorageData(),browserSession.clearCache(),browserSession.clearAuthCache()]);
    await flushSavedData();
    dataStatus='Saved cookies/site data cleared. New logins save in BrowserData.';
  }catch{dataStatus='Could not clear all data. Close the game and remove BrowserData manually.';}
  finally{clearingData=false;if(!quitting&&root){createTab(HOME,root);broadcast();}}
}
function shutdown(){if(quitting)return;quitting=true;clearQueuedAudio();if(audioSock)audioSock.destroy();flushSavedData().catch(()=>{}).finally(()=>app.quit());setTimeout(()=>app.exit(0),2500);}
if(PARENT)setInterval(()=>{try{process.kill(PARENT,0);}catch{shutdown();}},2000);
app.on('will-quit',wipe);process.on('exit',wipe);app.on('window-all-closed',()=>{});
function createTab(url=HOME,g=groups.get(0)){
  if(!g||totalTabs()>=10){broadcast();return;}
  const tabWindow=new BrowserWindow({width:g.width,height:g.height,useContentSize:true,show:false,frame:false,skipTaskbar:true,backgroundColor:'#15171c',webPreferences:{offscreen:true,session:browserSession,preload:path.join(__dirname,'preload.js'),sandbox:true,contextIsolation:true,nodeIntegration:false,webSecurity:true,allowRunningInsecureContent:false,webviewTag:false,disableHtmlFullscreenWindowResize:true,v8CacheOptions:'none',backgroundThrottling:false,spellcheck:false}});
  const tab={id:nextTabId++,win:tabWindow,group:g,muted:false,fullscreen:false,closed:false,navigating:false,pendingFullscreen:false,documentGeneration:0,youtubeRelaxed:false,recoveryAttempts:0,playbackStatus:'',crashTimes:[]};g.tabs.push(tab);const wc=tabWindow.webContents;
  for(const method of ['executeJavaScript','insertCSS']){const original=wc[method].bind(wc);wc[method]=(...args)=>original(...args).catch(()=>undefined);}
  wc.setZoomFactor&&wc.setZoomFactor(1);wc.setVisualZoomLevelLimits&&wc.setVisualZoomLevelLimits(1,1).catch(()=>{});
  wc.setUserAgent(wc.getUserAgent().replace(/ Electron\/\S+/,'').replace(/ TheInternet\/\S+/i,'').replace(/ the-internet\/\S+/i,''));
  wc.on('paint',(e,dirty,image)=>{if(!tab.closed&&current(tab.group)===tab){const s=image.getSize();if(s.width===tab.group.width&&s.height===tab.group.height)sendFrame(image,tab.group.id);}});
  wc.setWindowOpenHandler(({url})=>{if(/^https?:\/\//i.test(url)&&totalTabs()<10)createTab(url,tab.group);return {action:'deny'};});
  const guardNavigation=(e,url)=>{if(url!==HOME&&!/^https?:\/\//i.test(url))e.preventDefault();};
  wc.on('will-navigate',guardNavigation);wc.on('will-redirect',guardNavigation);
  wc.on('will-attach-webview',e=>e.preventDefault());
  wc.on('dom-ready',()=>{try{const url=new URL(wc.getURL());if((url.hostname==='google.com'||url.hostname==='www.google.com')&&/^\/(?:search|webhp)?\/?$/.test(url.pathname))wc.insertCSS(fs.readFileSync(path.join(__dirname,'assets','google-dark.css'),'utf8'),{cssOrigin:'user'}).catch(()=>{});}catch{}});
  wc.on('enter-html-full-screen',()=>{if(current(tab.group)!==tab){leaveFullscreen(tab);return;}if(tab.navigating){tab.pendingFullscreen=true;leaveFullscreen(tab);}else{tab.fullscreen=true;tab.group.height=FULLSCREEN_HEIGHT;settleViewport(tab);}state(tab.group);});wc.on('leave-html-full-screen',()=>{tab.fullscreen=false;if(current(tab.group)===tab){tab.group.height=HEIGHT;settleViewport(tab);state(tab.group);}});
  for(const ev of ['did-navigate','did-navigate-in-page','page-title-updated','did-start-loading','did-stop-loading','audio-state-changed'])wc.on(ev,()=>{if(!tab.closed)state(tab.group);});
  wc.on('did-start-navigation',(e,url,inPlace,mainFrame)=>{if(tab.closed)return;if(mainFrame&&!inPlace){tab.navigating=true;tab.documentGeneration++;leaveFullscreen(tab);state(tab.group);}if(mainFrame&&url!==tab.lastNavigation){tab.lastNavigation=url;tab.recoveryAttempts=0;if(!youtube(url)){tab.youtubeRelaxed=false;tab.playbackStatus='';}}if(current(tab.group)===tab)sendEvent({t:'focus',windowId:tab.group.id,editable:false});});
  wc.on('render-process-gone',()=>{if(tab.closed||quitting)return;const now=Date.now();tab.crashTimes=tab.crashTimes.filter(t=>now-t<60000);if(tab.crashTimes.length>=2){tab.playbackStatus='Browser renderer stopped repeatedly. Use Restart Internet.';state(tab.group);return;}tab.crashTimes.push(now);setTimeout(()=>reloadTab(tab,false),500);});
  wc.on('did-finish-load',()=>{if(!tab.closed){tab.navigating=false;wc.send('ti-youtube-config',youtubeConfig(tab));wc.send('ti-spatial-audio',current(tab.group)===tab&&tab.group.spatial);settleViewport(tab);if(tab.pendingFullscreen)setTimeout(()=>queuedFullscreen(tab),150);state(tab.group);}});
  activateTab(g.tabs.length-1,g);wc.loadURL(url).catch(()=>{});rates();broadcast();
}
function youtubeConfig(tab){return {enabled:!!tab&&adBlockEnabled,advanced:!!tab&&!tab.youtubeRelaxed};}
function findTab(sender){for(const g of groups.values())for(const tab of g.tabs)if(tab.win.webContents===sender)return tab;return null;}
app.whenReady().then(()=>{
  nativeTheme.themeSource='dark';browserSession=session.fromPartition('persist:the-internet-local',{cache:false});blocker=ElectronBlocker.deserialize(fs.readFileSync(path.join(__dirname,'filters.bin')));installCompatibleBlocker();blocker.enableBlockingInSession(browserSession);
  let cookieFlushPending=false;
  browserSession.cookies.on('changed',()=>{if(cookieFlushPending||quitting)return;cookieFlushPending=true;setTimeout(()=>{cookieFlushPending=false;if(!quitting)flushSavedData().catch(()=>{});},1000);});
  setInterval(()=>{if(!quitting&&!clearingData)flushSavedData().catch(()=>{});},10000);
  browserSession.setPermissionRequestHandler((wc,p,cb)=>cb(p==='fullscreen'||p==='persistent-storage'));browserSession.setPermissionCheckHandler((wc,p)=>p==='fullscreen'||p==='persistent-storage');browserSession.on('will-download',e=>e.preventDefault());browserSession.setSpellCheckerEnabled&&browserSession.setSpellCheckerEnabled(false);
  browserSession.setDevicePermissionHandler&&browserSession.setDevicePermissionHandler(()=>false);
  browserSession.on('file-system-access-restricted',e=>e.preventDefault());
  ipcMain.on('ti-audio',(e,samples)=>{const tab=findTab(e.sender);if(!tab||current(tab.group)!==tab||!tab.group.spatial||!ArrayBuffer.isView(samples)||samples.BYTES_PER_ELEMENT!==4||samples.length<2||samples.length>8192||samples.length%2)return;
    const header=Buffer.alloc(16);header.writeInt32LE(48000,0);header.writeInt32LE(2,4);header.writeInt32LE(tab.group.id,8);header.writeInt32LE(tab.id,12);
    if(audioQueue.length>=32)audioQueue.shift();audioQueue.push({windowId:tab.group.id,payload:Buffer.concat([header,Buffer.from(samples.buffer,samples.byteOffset,samples.byteLength)])});flushAudio();});
  ipcMain.on('ti-audio-status',(e,status)=>{const tab=findTab(e.sender);if(!tab||current(tab.group)!==tab||typeof status!=='string'||status.length>100)return;tab.group.audioStatus=status;state(tab.group);});
  ipcMain.on('ti-audio-backend',(e,backend)=>{const tab=findTab(e.sender);if(!tab||current(tab.group)!==tab||!['worklet','legacy'].includes(backend))return;tab.group.audioBackend=backend;state(tab.group);});
  ipcMain.on('ti-focus',(e,editable)=>{const tab=findTab(e.sender);if(tab&&current(tab.group)===tab)sendEvent({t:'focus',windowId:tab.group.id,editable:!!editable});});
  ipcMain.on('ti-youtube-config',e=>{e.returnValue=youtubeConfig(findTab(e.sender));});
  ipcMain.on('ti-playback-error',e=>{const tab=findTab(e.sender);if(tab&&current(tab.group)===tab)recoverPlayback(tab);});
  makeGroup(0);connect();connectAudio();createTab();
});
