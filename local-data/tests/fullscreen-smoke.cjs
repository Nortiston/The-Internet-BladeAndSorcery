'use strict';
// Developer-only fixture. Instrumentation is added to an isolated copy, never the release app.
const fs=require('node:fs'),net=require('node:net'),http=require('node:http'),path=require('node:path'),os=require('node:os'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
if(!process.argv[2])throw Error('Pass the staged runtime directory. See README.md.');
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'TheInternet-fullscreen-test-'));
fs.cpSync(path.resolve(process.argv[2]),temp,{recursive:true});
const main=path.join(temp,'resources/app/main.js');
let code=fs.readFileSync(main,'utf8');
code=code.replace("case 'hello':break;","case 'fixtureEval':if(wc)wc.executeJavaScript(cmd.code,true).then(value=>sendEvent({t:'fixtureEval',id:cmd.id,value}));break;case 'hello':break;");
fs.writeFileSync(main,code);
let child,socket,buffer=Buffer.alloc(0),state,req=0,requests=0,frame;const waiting=new Map();
const delay=ms=>new Promise(r=>setTimeout(r,ms));
function send(o){const p=Buffer.from(JSON.stringify(o)),h=Buffer.alloc(5);h[0]=1;h.writeUInt32LE(p.length,1);socket.write(Buffer.concat([h,p]));}
function probe(code){return new Promise((resolve,reject)=>{const id=++req;waiting.set(id,resolve);send({t:'fixtureEval',id,code});setTimeout(()=>{if(waiting.delete(id))reject(Error('fixture response timeout'));},5000).unref();});}
const page=`<!doctype html><style>html,body{margin:0;width:100%;height:100%;background:#121212}#movie_player{background:linear-gradient(90deg,#c00 50%,#00c 50%);width:100%;height:100%;}#movie_player:fullscreen{width:100vw;height:100vh}button{position:absolute;top:10px;left:10px}</style><div id="movie_player"><button onclick="this.parentNode.requestFullscreen()">Fullscreen</button></div><title>Fullscreen fixture</title>`;
const fixture=http.createServer((req,res)=>{const wait=++requests===1?0:350;setTimeout(()=>{res.writeHead(200,{'Content-Type':'text/html','Cache-Control':'no-store'});res.end(page);},wait);});
let running=false;
const server=net.createServer(s=>{socket=s;s.on('data',data=>{buffer=Buffer.concat([buffer,data]);while(buffer.length>=5&&buffer.length>=5+buffer.readUInt32LE(1)){
  const type=buffer[0],n=buffer.readUInt32LE(1),p=buffer.subarray(5,5+n);buffer=buffer.subarray(5+n);
  if(type===2){frame=[p.readInt32LE(0),p.readInt32LE(4)];continue;}
  if(type!==1)continue;const e=JSON.parse(p);
  if(e.t==='state'){state=e;if(!running&&e.title==='The Internet'&&!e.loading){running=true;run().catch(fail);}}
  else if(e.t==='fixtureEval'){const resolve=waiting.get(e.id);if(resolve){waiting.delete(e.id);resolve(e.value);}}
}});});
async function ready(){for(let i=0;i<60;i++){if(state?.title==='Fullscreen fixture'&&!state.loading)return;await delay(100);}throw Error('fixture did not load');}
async function snapshot(){return probe(`({viewport:[innerWidth,innerHeight],zoom:visualViewport.scale,full:!!document.fullscreenElement,rect:[document.querySelector('#movie_player').getBoundingClientRect().width,document.querySelector('#movie_player').getBoundingClientRect().height]})`);}
async function run(){
  send({t:'nav',url:'http://127.0.0.1:'+fixture.address().port+'/fixture'});await ready();
  for(let i=0;i<3;i++){
    await probe(`setTimeout(()=>document.querySelector('#movie_player').requestFullscreen().catch(()=>{}),50);true`);
    send({t:'reload'});await delay(2000);await ready();
    const s=await snapshot();assert.equal(s.full,true,'fullscreen request during refresh should settle');assert.deepEqual(s.viewport,[1280,858]);assert.deepEqual(s.rect,[1280,858]);assert.equal(s.zoom,1);assert.deepEqual(frame,[1280,858]);
    // A late old normal-size command must not overwrite the current fullscreen viewport.
    send({t:'resize',w:1280,h:720,fullscreen:false});await delay(200);assert.deepEqual((await snapshot()).viewport,[1280,858]);
    send({t:'key',key:'Escape'});await delay(350);const normal=await snapshot();assert.equal(normal.full,false);assert.deepEqual(normal.viewport,[1280,720]);
  }
  console.log('PASS: three real refresh/fullscreen races settle at 1280x858 with zoom 1, correct frame bounds, stale resize rejected, and normal viewport restored.');
  send({t:'quit'});
}
function fail(error){console.error(error);process.exitCode=1;child?.kill();socket?.destroy();server.close();fixture.close();}
const deadline=setTimeout(()=>fail(Error('fullscreen fixture deadline')),25000);
fixture.listen(0,'127.0.0.1',()=>server.listen(0,'127.0.0.1',()=>{
  child=spawn(path.join(temp,'TheInternetBrowser.exe'),['--port='+server.address().port,'--token=fullscreen-fixture','--parent='+process.pid,'--fps=10','--data-dir='+path.join(temp,'BrowserData')],{windowsHide:true,stdio:['ignore','ignore','pipe']});
  child.stderr.on('data',d=>process.stderr.write(d));
  child.on('exit',async exitCode=>{
    clearTimeout(deadline);socket?.destroy();server.close();fixture.close();if(exitCode!==0)process.exitCode=1;
    if(!path.resolve(temp).startsWith(path.resolve(os.tmpdir())+path.sep)||!path.basename(temp).startsWith('TheInternet-fullscreen-test-'))throw Error('invalid fixture cleanup');
    for(let i=0;i<20;i++){try{fs.rmSync(temp,{recursive:true,force:true});break;}catch{await delay(250);}}
  });
}));
