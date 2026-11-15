'use strict';
// Developer-only, deterministic YouTube-origin fixture. The HTTPS handler and
// evaluator are inserted into a temporary runtime copy, never the release.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),net=require('node:net'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
if(!process.argv[2])throw Error('Pass a staged runtime directory.');
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'TheInternet-youtube-test-'));
fs.cpSync(path.resolve(process.argv[2]),temp,{recursive:true});
const data={videoDetails:{videoId:'fixture'},playabilityStatus:{status:'OK'},streamingData:{formats:[{url:'https://content.invalid/video'}]},adSlots:[{}],adPlacements:[{}],playerAds:[{}]};
const html=`<!doctype html><title>YouTube blocker fixture</title><script>
ytInitialPlayerResponse=${JSON.stringify(data)};
window.initialHadAd='adSlots' in ytInitialPlayerResponse;
window.contentOK=ytInitialPlayerResponse.streamingData.formats[0].url==='https://content.invalid/video';
</script><div id="movie_player"><video></video><button class="ytp-ad-skip-button" onclick="window.skipped=(window.skipped||0)+1">Skip ad</button></div>`;
const handler=`browserSession.protocol.handle('https',request=>{const u=new URL(request.url);return new Response(u.pathname.startsWith('/youtubei/')||u.pathname==='/ordinary.json'?${JSON.stringify(JSON.stringify(data))}:${JSON.stringify(html)},{headers:{'Content-Type':u.pathname.startsWith('/youtubei/')||u.pathname==='/ordinary.json'?'application/json':'text/html','Cache-Control':'no-store'}});});`;
const main=path.join(temp,'resources/app/main.js');let code=fs.readFileSync(main,'utf8');
assert.ok(code.includes("case 'hello':break;"));
code=code.replace("case 'hello':break;","case 'fixtureEval':if(wc)wc.executeJavaScript(cmd.code,true).then(value=>sendEvent({t:'fixtureEval',id:cmd.id,value}));break;case 'hello':break;");
assert.ok(code.includes('  makeGroup(0);'));code=code.replace('  makeGroup(0);',handler+'\n  makeGroup(0);');fs.writeFileSync(main,code);
let child,socket,buffer=Buffer.alloc(0),state,request=0,running=false,completed=false;const pending=new Map();
const delay=ms=>new Promise(r=>setTimeout(r,ms));
function send(obj){const p=Buffer.from(JSON.stringify(obj)),h=Buffer.alloc(5);h[0]=1;h.writeUInt32LE(p.length,1);socket.write(Buffer.concat([h,p]));}
function probe(code){return new Promise((resolve,reject)=>{const id=++request;pending.set(id,resolve);send({t:'fixtureEval',id,code});setTimeout(()=>{if(pending.delete(id))reject(Error('probe timed out'));},6000).unref();});}
async function ready(){for(let i=0;i<80;i++){if(state?.title==='YouTube blocker fixture'&&!state.loading)return;await delay(100);}throw Error('fixture did not load');}
const server=net.createServer(s=>{socket=s;s.on('data',chunk=>{buffer=Buffer.concat([buffer,chunk]);while(buffer.length>=5&&buffer.length>=5+buffer.readUInt32LE(1)){
  const t=buffer[0],n=buffer.readUInt32LE(1),p=buffer.subarray(5,5+n);buffer=buffer.subarray(5+n);if(t!==1)continue;const e=JSON.parse(p);
  if(e.t==='fixtureEval'){const done=pending.get(e.id);if(done){pending.delete(e.id);done(e.value);}}
  if(e.t==='state'){state=e;if(!running&&e.title==='The Internet'&&!e.loading){running=true;run().catch(fail);}}
}});});
async function run(){
  send({t:'nav',url:'https://www.youtube.com/watch?v=fixture'});await ready();
  assert.deepEqual(await probe('({initialHadAd,contentOK,node:typeof require,ipc:typeof ipcRenderer})'),{initialHadAd:false,contentOK:true,node:'undefined',ipc:'undefined'});
  assert.equal(await probe('(async()=>!!(await (await fetch("/youtubei/v1/player")).json()).adSlots)()'),false);
  assert.equal(await probe('(async()=>!!(await (await fetch("/ordinary.json")).json()).adSlots)()'),true);
  assert.equal(await probe('new Promise(resolve=>{const x=new XMLHttpRequest();x.open("GET","/youtubei/v1/player");x.responseType="json";x.onload=()=>resolve(!!x.response.adSlots);x.send()})'),false);
  await probe('document.querySelector("#movie_player").classList.add("ad-showing");true');await delay(350);
  assert.equal(await probe('(window.skipped||0)>0'),true);assert.equal(await probe('document.querySelector("video").muted'),true);
  await probe('document.querySelector("#movie_player").classList.remove("ad-showing");true');await delay(350);assert.equal(await probe('document.querySelector("video").muted'),false);
  // Main-process toggle must reach the next document-start preload.
  send({t:'adblock',v:false});await delay(600);await ready();assert.equal(await probe('initialHadAd'),true);
  assert.equal(await probe('Boolean(document.getElementById("the-internet-youtube-ads"))'),false);
  send({t:'adblock',v:true});await delay(600);await ready();assert.equal(await probe('initialHadAd'),false);
  // Recovery drops only response hooks; the network filter still cancels ad URLs.
  await probe('document.body.insertAdjacentHTML("beforeend",\'<div class="ytp-error-content-wrap">Something went wrong. Refresh or try again later</div>\');true');
  for(let i=0;i<50&&!state.youtubeCompatibility;i++)await delay(100);
  assert.equal(state.youtubeCompatibility,true);await delay(900);await ready();assert.equal(await probe('initialHadAd'),true);
  assert.equal(await probe('window[Symbol.for("TheInternet.YouTubeProtection")].summary().advanced'),false);
  assert.match(state.playbackStatus,/network blocking stays on/);
  await probe('document.querySelector("#movie_player").classList.add("ad-showing");true');await delay(350);assert.equal(await probe('(window.skipped||0)>0'),true);
  completed=true;console.log('PASS: sandboxed Electron document-start player cleanup, real fetch/XHR, content preservation, scoped ad skip/mute restoration, off/on reload and bounded compatibility recovery.');send({t:'quit'});
}
function fail(error){console.error(error);process.exitCode=1;child?.kill();socket?.destroy();server.close();}
const deadline=setTimeout(()=>fail(Error('YouTube fixture deadline')),30000);
server.listen(0,'127.0.0.1',()=>{
  const args=['--port='+server.address().port,'--token=youtube-fixture','--parent='+process.pid,'--fps=3'];
  if(fs.readFileSync(main,'utf8').includes("const DATA_DIR = arg("))args.push('--data-dir='+path.join(temp,'BrowserData'));
  child=spawn(path.join(temp,'TheInternetBrowser.exe'),args,{windowsHide:true,stdio:['ignore','ignore','pipe']});
  child.stderr.on('data',d=>process.stderr.write(d));
  child.on('exit',async exitCode=>{clearTimeout(deadline);socket?.destroy();server.close();if(exitCode!==0||!completed){console.error('YouTube fixture exited before completion:',exitCode);process.exitCode=1;}
    if(!path.resolve(temp).startsWith(path.resolve(os.tmpdir())+path.sep)||!path.basename(temp).startsWith('TheInternet-youtube-test-'))throw Error('invalid fixture cleanup');
    for(let i=0;i<30;i++){try{fs.rmSync(temp,{recursive:true,force:true});break;}catch{await delay(250);}}
  });
});
