'use strict';
const {test}=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm');
const {installYoutubeBlocker}=require('./youtube-blocker.cjs');
function fixture(config={enabled:true,advanced:true},hostname='www.youtube.com'){
  let interval,intervals=0,removed=0,clicks=0,ad=false,skip=false;
  const video={muted:false,duration:30,currentTime:2};
  const player={classList:{contains:n=>ad&&n==='ad-showing'},querySelector:s=>s==='video'?video:skip?{disabled:false,getAttribute:()=>null,click(){clicks++;}}:null};
  class Response{constructor(value,url){this.value=value;this.url=url;}json(){return Promise.resolve(JSON.parse(JSON.stringify(this.value)));}text(){return Promise.resolve(JSON.stringify(this.value));}}
  class XMLHttpRequest{open(method,url){this.url=url;}get responseText(){if(this.responseType&&this.responseType!=='text')throw Error('InvalidStateError');return this.raw;}get response(){return this.responseType==='json'?this.value:this.raw;}}
  const context=vm.createContext({location:{hostname,href:'https://'+hostname+'/watch?v=fixture'},URL,Response,XMLHttpRequest,
    document:{head:{appendChild(){}},createElement:()=>({remove(){removed++;}}),getElementById:()=>player,querySelector:()=>player,addEventListener(){}},
    setInterval(f){interval=f;intervals++;},console});
  vm.runInContext('window=globalThis',context);
  function install(value){context.config=value;return vm.runInContext('('+installYoutubeBlocker.toString()+')(config)',context);}
  install(config);
  const evaluate=code=>vm.runInContext(code,context);
  return {evaluate,install,video,tick:()=>interval?.(),setAd:v=>ad=v,setSkip:v=>skip=v,get clicks(){return clicks;},get intervals(){return intervals;},get removed(){return removed;}};
}
const payload={videoDetails:{videoId:'content'},streamingData:{adaptiveFormats:[{url:'https://media.invalid/content'}]},playabilityStatus:{status:'OK'},playerAds:[{ad:1}],adPlacements:[{}],adSlots:[{}]};
test('initial player and inline JSON lose ad slots without changing playable content',()=>{
  const f=fixture();
  const p=f.evaluate('ytInitialPlayerResponse='+JSON.stringify(payload)+';ytInitialPlayerResponse');
  for(const name of ['playerAds','adPlacements','adSlots'])assert.equal(name in p,false);
  assert.equal(p.streamingData.adaptiveFormats[0].url,payload.streamingData.adaptiveFormats[0].url);
  assert.equal(p.playabilityStatus.status,'OK');
  const parsed=f.evaluate('JSON.parse('+JSON.stringify(JSON.stringify([{playerResponse:payload}]))+')');
  assert.equal(parsed[0].playerResponse.adSlots,undefined);
  f.evaluate('ytplayer={config:{args:{player_response:'+JSON.stringify(JSON.stringify(payload))+'}}}');
  assert.equal(JSON.parse(f.evaluate('ytplayer.config.args.player_response')).adSlots,undefined);
});
test('ordinary JSON, revivers, invalid JSON and non YouTube hosts retain native behavior',()=>{
  const f=fixture();
  assert.equal(f.evaluate('JSON.parse(\'{"adSlots":[1],"hello":2}\').adSlots[0]'),1);
  assert.equal(f.evaluate('JSON.parse(\'{"x":3}\',(k,v)=>k==="x"?v*2:v).x'),6);
  assert.throws(()=>f.evaluate('JSON.parse("broken")'),/JSON/);
  for(const host of ['example.com','youtube.com.evil.invalid']){
    const other=fixture({enabled:true},host);assert.equal(other.intervals,0);
    assert.equal(other.evaluate('JSON.parse('+JSON.stringify(JSON.stringify(payload))+').adSlots.length'),1);
  }
});
test('fetch responses are scoped to player endpoints; text and XHR preserve content',async()=>{
  const f=fixture();f.evaluate('data='+JSON.stringify(payload));
  assert.equal((await f.evaluate('new Response(data,"https://www.youtube.com/youtubei/v1/player?key=x").json()')).adSlots,undefined);
  assert.equal(JSON.parse(await f.evaluate('new Response(data,"https://www.youtube.com/youtubei/v1/next").text()')).adSlots,undefined);
  assert.equal((await f.evaluate('new Response(data,"https://unrelated.invalid/player").json()')).adSlots.length,1);
  f.evaluate('xhr=new XMLHttpRequest();xhr.open("POST","/youtubei/v1/player");xhr.raw='+JSON.stringify(JSON.stringify(payload))+';xhr.value=data');
  assert.equal(JSON.parse(f.evaluate('xhr.responseText')).adSlots,undefined);
  f.evaluate('xhr.responseType="json"');assert.equal(f.evaluate('xhr.response.adSlots'),undefined);
  assert.throws(()=>f.evaluate('xhr.responseText'),/InvalidStateError/);
  f.evaluate('xhr.responseType="arraybuffer";xhr.raw=new Uint8Array([1,2,3])');assert.equal(f.evaluate('xhr.response[1]'),2);
});
test('only explicitly marked ads are skipped or muted, and content mute state is restored',()=>{
  const f=fixture();f.tick();assert.equal(f.video.currentTime,2);assert.equal(f.video.muted,false);
  f.setAd(true);f.tick();assert.equal(f.video.currentTime,29.95);assert.equal(f.video.muted,true);
  f.setAd(false);f.tick();assert.equal(f.video.muted,false);
  f.video.currentTime=2;f.video.duration=Infinity;f.setAd(true);f.tick();assert.equal(f.video.currentTime,2);
  f.setSkip(true);f.tick();assert.equal(f.clicks,1);
  f.install({enabled:false});assert.equal(f.video.muted,false);assert.equal(f.removed,1);
});
test('compatibility mode leaves response data intact, keeps ad skipping, and never adds a second timer',()=>{
  const f=fixture();f.install({enabled:true,advanced:false});assert.equal(f.intervals,1);
  assert.equal(f.evaluate('JSON.parse('+JSON.stringify(JSON.stringify(payload))+').adSlots.length'),1);
  f.setAd(true);f.setSkip(true);f.tick();assert.equal(f.clicks,1);
  f.install({enabled:false});f.tick();assert.equal(f.clicks,1);
  assert.equal(f.evaluate('typeof require'), 'undefined');
  const off=fixture({enabled:false});assert.equal(off.intervals,0);
});
