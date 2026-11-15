'use strict';

// Self-contained: serialized into the page's main world by a sandboxed preload.
// No Node, IPC, filesystem, cookie or account API is exposed to the page.
function installYoutubeBlocker(config) {
  const host = location.hostname;
  if (!(host === 'youtube.com' || host.endsWith('.youtube.com') || host === 'youtube-nocookie.com' || host.endsWith('.youtube-nocookie.com'))) return {active:false};
  const key = Symbol.for('TheInternet.YouTubeProtection');
  const previous = window[key];
  if (previous) { previous.configure(config); return previous.summary(); }
  if (!config || !config.enabled) return {active:false};

  const state = {enabled:true, advanced:config.advanced !== false, responses:0, slots:0, skips:0};
  const parse = JSON.parse, stringify = JSON.stringify;
  const adKeys = ['playerAds','adPlacements','adSlots'];
  const childKeys = ['playerResponse','response','endpoint','watchEndpoint','unserializedPrefetchData','player'];
  const mediaState = new WeakMap();
  let lastVideo = null, lastSeek = 0, style;
  const styleId = 'the-internet-youtube-ads';
  const css = 'ytd-ad-slot-renderer,ytd-display-ad-renderer,ytd-promoted-sparkles-web-renderer,ytd-in-feed-ad-layout-renderer,ytd-action-companion-ad-renderer,ytm-promoted-sparkles-web-renderer,#masthead-ad,.ytp-ad-overlay-container{display:none!important}';

  function clean(root, force) {
    if (!state.enabled || !state.advanced || !root || typeof root !== 'object') return root;
    const seen = new WeakSet(), stack = [{value:root, force:!!force, depth:0}];
    let visited = 0, removed = 0;
    while (stack.length && visited++ < 128) {
      const item = stack.pop(), value = item.value;
      if (!value || typeof value !== 'object' || seen.has(value) || item.depth > 8) continue;
      seen.add(value);
      if (Array.isArray(value)) {
        for (let i=0;i<Math.min(value.length,128);i++) stack.push({value:value[i],force:false,depth:item.depth+1});
        continue;
      }
      if (item.force || value.videoDetails || value.playabilityStatus || value.streamingData) {
        for (const name of adKeys) if (Object.prototype.hasOwnProperty.call(value,name)) {
          try { if (delete value[name]) removed++; } catch (_) { }
        }
      }
      for (const name of childKeys) if (value[name] && typeof value[name] === 'object') stack.push({value:value[name],force:name==='playerResponse',depth:item.depth+1});
    }
    if (removed) { state.responses++; state.slots+=removed; }
    return root;
  }
  function cleanText(text) {
    if (!state.enabled || !state.advanced || typeof text !== 'string' || !adKeys.some(name=>text.includes('"'+name+'"'))) return text;
    try { const value=Reflect.apply(parse,JSON,[text]); const before=state.slots;clean(value,false);return before===state.slots?text:Reflect.apply(stringify,JSON,[value]); } catch (_) { return text; }
  }
  function playerRequest(url) {
    try {
      const u=new URL(url,location.href), h=u.hostname;
      return (h==='youtube.com'||h.endsWith('.youtube.com')||h==='youtube-nocookie.com'||h.endsWith('.youtube-nocookie.com')) &&
        (/^\/youtubei\/v1\/(?:player|next|navigation\/resolve_url)$/.test(u.pathname)||u.pathname==='/watch'||u.pathname==='/playlist');
    } catch (_) { return false; }
  }
  JSON.parse = new Proxy(parse,{apply(target,self,args){const value=Reflect.apply(target,self,args);return clean(value,false);}});
  if (typeof Response !== 'undefined') for (const method of ['json','text']) {
    const original=Response.prototype[method]; if(typeof original!=='function')continue;
    Response.prototype[method]=new Proxy(original,{apply(target,self,args){
      const promise=Reflect.apply(target,self,args);
      if(!state.enabled||!state.advanced||!playerRequest(self.url))return promise;
      return promise.then(value=>method==='json'?clean(value,false):cleanText(value));
    }});
  }
  if (typeof XMLHttpRequest !== 'undefined') {
    const urls=new WeakMap(), open=XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open=new Proxy(open,{apply(target,self,args){urls.set(self,String(args[1]));return Reflect.apply(target,self,args);}});
    for(const name of ['responseText','response']){
      const descriptor=Object.getOwnPropertyDescriptor(XMLHttpRequest.prototype,name);
      if(!descriptor||!descriptor.get||descriptor.configurable===false)continue;
      const get=descriptor.get;
      Object.defineProperty(XMLHttpRequest.prototype,name,{...descriptor,get(){
        const value=Reflect.apply(get,this,[]);
        if(!state.enabled||!state.advanced||!playerRequest(urls.get(this)||''))return value;
        if(name==='responseText'||!this.responseType||this.responseType==='text')return cleanText(value);
        return this.responseType==='json'?clean(value,false):value;
      }});
    }
  }
  function initialPlayer(value){
    clean(value,true);
    const args=value&&value.config&&value.config.args;
    if(args&&typeof args.player_response==='string')args.player_response=cleanText(args.player_response);
    return value;
  }
  for(const name of ['ytInitialPlayerResponse','ytplayer']){
    try{
      const descriptor=Object.getOwnPropertyDescriptor(window,name);
      if(descriptor&&(descriptor.configurable===false||descriptor.writable===false)) {initialPlayer(window[name]);continue;}
      let value=initialPlayer(window[name]);
      Object.defineProperty(window,name,{configurable:true,enumerable:descriptor?descriptor.enumerable:true,
        get(){return initialPlayer(descriptor&&descriptor.get?Reflect.apply(descriptor.get,this,[]):value);},
        set(next){next=initialPlayer(next);if(descriptor&&descriptor.set)Reflect.apply(descriptor.set,this,[next]);else value=next;}});
    }catch(_){ }
  }
  function restore(video){const old=video&&mediaState.get(video);if(old){if(old.mutedByUs&&video.muted)video.muted=old.muted;mediaState.delete(video);}}
  function applyStyle(){
    if(!state.enabled){if(style){style.remove();style=null;}return;}
    if(!style&&(document.head||document.documentElement)){style=document.createElement('style');style.id=styleId;style.textContent=css;(document.head||document.documentElement).appendChild(style);}
  }
  function tick(){
    try{
      applyStyle();
      const player=document.getElementById('movie_player')||document.querySelector('.html5-video-player');
      const video=player&&player.querySelector('video');
      if(lastVideo&&lastVideo!==video)restore(lastVideo);lastVideo=video;
      if(!state.enabled||!player||!video){restore(video);return;}
      const ad=player.classList.contains('ad-showing')||player.classList.contains('ad-interrupting');
      if(!ad){restore(video);return;}
      if(!mediaState.has(video))mediaState.set(video,{muted:video.muted,mutedByUs:!video.muted});
      if(!video.muted)video.muted=true;
      const skip=player.querySelector('.ytp-ad-skip-button,.ytp-ad-skip-button-modern,.ytp-skip-ad-button,.ytp-ad-skip-button-container button');
      if(skip&&!skip.disabled&&skip.getAttribute('aria-disabled')!=='true'){skip.click();state.skips++;return;}
      // Seek only during an explicitly identified finite ad; never touch live/infinite media.
      const now=Date.now();
      if(Number.isFinite(video.duration)&&video.duration>0&&video.duration<3600&&now-lastSeek>750){
        if(video.currentTime<video.duration-0.15){video.currentTime=Math.max(0,video.duration-0.05);lastSeek=now;state.skips++;}
      }
    }catch(_){ }
  }
  const api={configure(next){state.enabled=!!(next&&next.enabled);state.advanced=!!(next&&next.advanced!==false);if(!state.enabled)restore(lastVideo);applyStyle();},summary(){return {active:state.enabled,advanced:state.advanced,responses:state.responses,slots:state.slots,skips:state.skips};}};
  Object.defineProperty(window,key,{value:api,configurable:true});
  document.addEventListener('DOMContentLoaded',tick,{once:true});
  document.addEventListener('yt-navigate-finish',tick);
  setInterval(tick,250);tick();
  return api.summary();
}

module.exports={installYoutubeBlocker};
