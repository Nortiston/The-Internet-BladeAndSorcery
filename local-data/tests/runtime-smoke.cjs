const net=require('node:net'),http=require('node:http'),path=require('node:path'),assert=require('node:assert/strict'),fs=require('node:fs'),os=require('node:os');
const {spawn}=require('node:child_process');
const profileTestRoot=fs.mkdtempSync(path.join(os.tmpdir(),'TheInternet-local-smoke-'));
let child,socket,stage='home',firstUrl,loads=0;const frames=new Set();
const fixture=http.createServer((req,res)=>{if(req.url==='/first')loads++;res.writeHead(200,{'Content-Type':'text/html'});res.end('<title>First tab</title><body style="background:#15171c;color:white">Local tab fixture</body>');});
function send(o){const p=Buffer.from(JSON.stringify(o)),h=Buffer.alloc(5);h[0]=1;h.writeUInt32LE(p.length,1);socket.write(Buffer.concat([h,p]));}
const server=net.createServer(s=>{socket=s;let buf=Buffer.alloc(0);s.on('data',d=>{buf=Buffer.concat([buf,d]);while(buf.length>=5&&buf.length>=5+buf.readUInt32LE(1)){
 const type=buf[0],len=buf.readUInt32LE(1),p=buf.subarray(5,5+len);buf=buf.subarray(5+len);
 if(type===2){const id=p.readInt32LE(8);assert.equal(p.length,12+p.readInt32LE(0)*p.readInt32LE(4)*4);frames.add(id);if(stage==='frame1'&&id===1){stage='join';send({t:'moveTab',windowId:1,index:0,targetWindowId:0});}else if(stage==='frame2'&&id===2){stage='merge';send({t:'mergeWindows',preferredWindowId:2});}continue;}
 if(type!==1)continue;const e=JSON.parse(p);if(e.t!=='state')continue;assert.ok(e.totalTabs<=10);
 if(stage==='home'&&e.title==='The Internet'&&!e.loading){stage='first';send({t:'floating',v:true});send({t:'nav',url:firstUrl});}
 else if(stage==='first'&&e.title==='First tab'&&!e.loading){stage='create';for(let i=0;i<9;i++)send({t:'newTab'});}
 else if(stage==='create'&&e.windowId===0&&e.tabs.length===10){stage='cap';send({t:'newTab'});}
 else if(stage==='cap'&&e.windowId===0&&e.tabs.length===10){stage='detach';send({t:'detachTab',index:0,position:{x:1,y:1,z:1}});}
 else if(stage==='detach'&&e.windowId===1){assert.equal(e.url,firstUrl);assert.equal(e.tabs.length,1);stage='frame1';}
 else if(stage==='join'&&e.windowId===0&&e.tabs.length===10){assert.equal(e.url,firstUrl);assert.ok(e.canBack);assert.equal(loads,1);stage='split';send({t:'detachTab',index:9});}
 else if(stage==='split'&&e.windowId===2){assert.equal(e.url,firstUrl);stage='frame2';}
 else if(stage==='merge'&&e.windowId===0&&e.tabs.length===10){assert.equal(e.isFloating,false);assert.equal(e.url,firstUrl);assert.equal(loads,1);stage='mute';send({t:'mute',v:true});}
 else if(stage==='mute'&&e.windowId===0&&e.muted){stage='other';send({t:'selectTab',index:0});}
 else if(stage==='other'&&e.windowId===0&&e.activeTab===0){assert.equal(e.muted,false);stage='back';send({t:'selectTab',index:9});}
 else if(stage==='back'&&e.windowId===0&&e.activeTab===9){assert.equal(e.muted,true);stage='close';send({t:'closeTab',index:9});}
 else if(stage==='close'&&e.windowId===0&&e.tabs.length===9){stage='all';for(let i=0;i<8;i++)send({t:'closeTab',index:0});}
 else if(stage==='all'&&e.windowId===0&&e.tabs.length===1){stage='last';send({t:'closeTab',index:0});}
 else if(stage==='last'&&e.windowId===0&&e.tabs.length===1&&!e.loading&&e.title==='The Internet'){stage='done';send({t:'quit'});}
}});});
const timeout=setTimeout(()=>{console.error('Window smoke timed out',stage,[...frames]);child?.kill();socket?.destroy();server.close();fixture.close();process.exitCode=1;},30000);
fixture.listen(0,'127.0.0.1',()=>{firstUrl='http://127.0.0.1:'+fixture.address().port+'/first';server.listen(0,'127.0.0.1',()=>{
 if(!process.argv[2])throw Error('Pass the staged runtime directory. See README.md.');const runtime=path.resolve(process.argv[2]);
 child=spawn(path.join(runtime,'TheInternetBrowser.exe'),['--port='+server.address().port,'--token=test','--parent='+process.pid,'--data-dir='+path.join(profileTestRoot,'BrowserData')],{windowsHide:true,stdio:['ignore','ignore','pipe']});child.stderr.on('data',d=>process.stderr.write(d));
 child.on('exit',async code=>{clearTimeout(timeout);socket?.destroy();server.close();fixture.close();assert.equal(code,0);assert.equal(stage,'done');assert.equal(loads,1);assert.ok(frames.has(0)&&frames.has(1)&&frames.has(2));
 const temp=path.join(os.tmpdir(),'TheInternet-BAS');let left=[];for(let i=0;i<30;i++){left=fs.existsSync(temp)?fs.readdirSync(temp).filter(n=>n.startsWith('run-'+child.pid+'-')):[];if(!left.length)break;await new Promise(r=>setTimeout(r,500));}assert.equal(left.length,0);
 console.log('PASS: ten real tabs; independent window images; detach/rejoin/dock merge preserve page and history without reload; mute, close and cleanup pass.');});
 child.on('exit',async()=>{if(!path.resolve(profileTestRoot).startsWith(path.resolve(os.tmpdir())+path.sep)||!path.basename(profileTestRoot).startsWith('TheInternet-local-smoke-'))throw Error('Invalid profile test cleanup');for(let i=0;i<20;i++){try{fs.rmSync(profileTestRoot,{recursive:true,force:true});break;}catch{await new Promise(r=>setTimeout(r,250));}}});
});});
