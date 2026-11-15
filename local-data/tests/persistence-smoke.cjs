'use strict';
// Uses generated local data only. No real accounts or external websites.
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),http=require('node:http'),net=require('node:net'),assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
if(!process.argv[2])throw Error('Pass a staged Local Data runtime directory.');
const root=fs.mkdtempSync(path.join(os.tmpdir(),'TheInternet-local-data-test-'));
const runtime=path.join(root,'Runtime'),data=path.join(root,'BrowserData');
fs.cpSync(path.resolve(process.argv[2]),runtime,{recursive:true});
const main=path.join(runtime,'resources/app/main.js');
fs.writeFileSync(main,fs.readFileSync(main,'utf8').replace("case 'hello':break;","case 'fixtureEval':if(wc)wc.executeJavaScript(cmd.code,true).then(value=>sendEvent({t:'fixtureEval',id:cmd.id,value}));break;case 'hello':break;"));
let live,port,requests=0;
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const page=`<!doctype html><title>Local Data fixture</title><body>Generated persistence test</body>`;
const fixture=http.createServer((req,res)=>{
  if(req.url==='/cookie'){res.writeHead(200,{'Content-Type':'application/json'});res.end(JSON.stringify({signedIn:/(?:^|; )fixture_login=present(?:;|$)/.test(req.headers.cookie||'')}));return;}
  if(req.url==='/seed'){requests++;res.setHeader('Set-Cookie','fixture_login=present; Path=/; Max-Age=3600; HttpOnly; SameSite=Lax');}
  res.writeHead(200,{'Content-Type':'text/html','Cache-Control':'no-store'});res.end(page);
});
async function until(check,label){for(let i=0;i<80;i++){if(check())return;await delay(100);}throw Error('Timed out: '+label);}
async function launch(){
  const pending=new Map();let socket,buffer=Buffer.alloc(0),id=0,state,child;
  const server=net.createServer(s=>{socket=s;s.on('data',d=>{buffer=Buffer.concat([buffer,d]);while(buffer.length>=5&&buffer.length>=5+buffer.readUInt32LE(1)){
    const t=buffer[0],n=buffer.readUInt32LE(1),p=buffer.subarray(5,5+n);buffer=buffer.subarray(5+n);if(t!==1)continue;const e=JSON.parse(p);
    if(e.t==='state')state=e;
    if(e.t==='fixtureEval'){const resolve=pending.get(e.id);if(resolve){pending.delete(e.id);resolve(e.value);}}
  }});});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  child=spawn(path.join(runtime,'TheInternetBrowser.exe'),['--port='+server.address().port,'--token=local-data-fixture','--parent='+process.pid,'--fps=3','--data-dir='+data],{windowsHide:true,stdio:['ignore','ignore','pipe']});
  child.stderr.on('data',d=>process.stderr.write(d));
  let exited;const exit=new Promise(resolve=>child.on('exit',code=>{exited=code;resolve(code);}));
  function send(o){const p=Buffer.from(JSON.stringify(o)),h=Buffer.alloc(5);h[0]=1;h.writeUInt32LE(p.length,1);socket.write(Buffer.concat([h,p]));}
  async function probe(code){return new Promise((resolve,reject)=>{const n=++id;pending.set(n,resolve);send({t:'fixtureEval',id:n,code});setTimeout(()=>{if(pending.delete(n))reject(Error('probe timeout'));},5000).unref();});}
  async function stop(){send({t:'quit'});assert.equal(await exit,0);socket?.destroy();await new Promise(resolve=>server.close(resolve));}
  async function navigate(where){send({t:'nav',url:'http://127.0.0.1:'+port+where});await until(()=>state?.title==='Local Data fixture'&&!state.loading,'fixture navigation');}
  const client={send,probe,stop,navigate,get state(){return state;},kill(){child.kill();socket?.destroy();server.close();}};
  live=client;await until(()=>state?.title==='The Internet'&&!state.loading,'browser startup; exit='+exited);
  assert.equal(state.localDataEnabled,true);return client;
}
const dbWrite=`new Promise((resolve,reject)=>{let r=indexedDB.open('fixture-db',1);r.onupgradeneeded=()=>r.result.createObjectStore('kv');r.onerror=()=>reject(Error('db open'));r.onsuccess=()=>{let db=r.result,tx=db.transaction('kv','readwrite');tx.objectStore('kv').put('saved','login');tx.oncomplete=()=>{db.close();resolve(true)}}})`;
const dbRead=`new Promise((resolve,reject)=>{let r=indexedDB.open('fixture-db',1);r.onupgradeneeded=()=>r.result.createObjectStore('kv');r.onerror=()=>reject(Error('db open'));r.onsuccess=()=>{let db=r.result,q=db.transaction('kv').objectStore('kv').get('login');q.onsuccess=()=>{let value=q.result;db.close();resolve(value||null)}}})`;
async function run(){
  await new Promise(resolve=>fixture.listen(0,'127.0.0.1',resolve));port=fixture.address().port;
  let client=await launch();await client.navigate('/seed');
  assert.equal(await client.probe(`localStorage.setItem('fixture','saved');true`),true);assert.equal(await client.probe(dbWrite),true);
  assert.equal(await client.probe(`fetch('/cookie').then(r=>r.json()).then(x=>x.signedIn)`),true);
  await client.stop();await delay(250);
  client=await launch();await client.navigate('/verify');
  assert.equal(await client.probe(`localStorage.getItem('fixture')`),'saved');assert.equal(await client.probe(dbRead),'saved');
  assert.equal(await client.probe(`fetch('/cookie').then(r=>r.json()).then(x=>x.signedIn)`),true);
  client.send({t:'clearData'});await until(()=>client.state?.title==='The Internet'&&!client.state.loading&&!client.state.clearingData,'clear saved data');
  await client.navigate('/verify');
  assert.equal(await client.probe(`localStorage.getItem('fixture')`),null);assert.equal(await client.probe(dbRead),null);
  assert.equal(await client.probe(`fetch('/cookie').then(r=>r.json()).then(x=>x.signedIn)`),false);
  await client.stop();await delay(250);
  client=await launch();await client.navigate('/verify');
  assert.equal(await client.probe(`fetch('/cookie').then(r=>r.json()).then(x=>x.signedIn)`),false);assert.equal(await client.probe(`localStorage.getItem('fixture')`),null);
  await client.stop();assert.equal(requests,1);assert.ok(fs.existsSync(path.join(data,'Profile')));
  console.log('PASS: HTTP-only login cookie, localStorage and IndexedDB survive a full process restart; Clear saved data signs out and stays cleared after another restart.');
}
const deadline=setTimeout(()=>{live?.kill();process.exitCode=1;console.error('Persistence test deadline');fixture.close();},35000);
run().catch(error=>{console.error(error);process.exitCode=1;live?.kill();}).finally(async()=>{
  clearTimeout(deadline);fixture.close();
  if(!path.resolve(root).startsWith(path.resolve(os.tmpdir())+path.sep)||!path.basename(root).startsWith('TheInternet-local-data-test-'))throw Error('Invalid test cleanup path');
  for(let i=0;i<20;i++){try{fs.rmSync(root,{recursive:true,force:true});break;}catch{await delay(250);}}
});
