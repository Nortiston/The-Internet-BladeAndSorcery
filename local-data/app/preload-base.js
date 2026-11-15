// Tells the mod when a text field gets or loses focus, so the in-game keyboard can pop up.
const { ipcRenderer, contextBridge } = require('electron');

// Install synchronously before YouTube's inline player data is consumed. Only a
// fixed, self-contained function crosses worlds; no privileged API is exposed.
function youtubeProtection(config) {
  try { contextBridge.executeInMainWorld({func:installYoutubeBlocker,args:[config]}); } catch (_) { }
}
if (/(^|\.)youtube(?:-nocookie)?\.com$/.test(location.hostname)) {
  youtubeProtection(ipcRenderer.sendSync('ti-youtube-config'));
  ipcRenderer.on('ti-youtube-config', (_event, config) => youtubeProtection(config));
}

function isEditable(el) {
  if (!el) return false;
  if (el.isContentEditable) return true;
  const tag = (el.tagName || '').toLowerCase();
  if (tag === 'textarea') return !el.readOnly && !el.disabled;
  if (tag === 'input') {
    const t = (el.type || 'text').toLowerCase();
    const textTypes = ['text', 'search', 'email', 'url', 'tel', 'password', 'number', ''];
    return textTypes.includes(t) && !el.readOnly && !el.disabled;
  }
  return false;
}

let last = null;
function report() {
  let el = document.activeElement;
  // look inside same-origin iframes / shadow roots
  while (el && el.shadowRoot && el.shadowRoot.activeElement) el = el.shadowRoot.activeElement;
  const editable = isEditable(el);
  if (editable !== last) {
    last = editable;
    ipcRenderer.send('ti-focus', editable);
  }
}

window.addEventListener('focusin', () => setTimeout(report, 0), true);
window.addEventListener('focusout', () => setTimeout(report, 0), true);
window.addEventListener('mousedown', () => setTimeout(report, 50), true);

// Opt-in PCM routing. Nothing is recorded or written to disk. Cross-origin
// elements without CORS and elements owned by a site's own audio graph keep
// their normal output instead of risking silent/broken playback.
let spatialEnabled = false;
let audioContext, dryOutput, tap, mediaObserver;
let audioInitialization;
const mediaNodes = new WeakMap();
let lastAudioStatus = '';
function audioStatus(text) {
  if (text !== lastAudioStatus) { lastAudioStatus = text; ipcRenderer.send('ti-audio-status', text); }
}
function safeMedia(element) {
  if (element.srcObject) return true;
  if (!element.currentSrc) return false;
  try {
    const url = new URL(element.currentSrc, location.href);
    return url.protocol === 'blob:' || url.origin === location.origin || !!element.crossOrigin;
  } catch { return false; }
}
function routeMedia() {
  if (!spatialEnabled || !audioContext) return;
  let routed = 0, fallback = false;
  for (const element of document.querySelectorAll('video,audio')) {
    if (mediaNodes.has(element)) { routed++; continue; }
    if (!safeMedia(element)) { fallback = true; continue; }
    try {
      const source = audioContext.createMediaElementSource(element);
      source.connect(dryOutput);
      source.connect(tap);
      mediaNodes.set(element, source);
      routed++;
    } catch { fallback = true; }
  }
  audioStatus(fallback ? 'Unsupported media keeps normal audio.' : routed ? 'Audio routed to screen.' : 'Waiting for supported page media.');
}
async function initializeAudio() {
  if (!audioInitialization) audioInitialization = (async () => {
      audioContext = new AudioContext({ sampleRate: 48000 });
      if (audioContext.sampleRate !== 48000) throw new Error('Unsupported sample rate');
      dryOutput = audioContext.createGain(); dryOutput.connect(audioContext.destination);
      const workletCode = `class InternetPcmTap extends AudioWorkletProcessor {
        constructor(){super();this.pcm=new Float32Array(4096);this.used=0;}
        process(inputs,outputs){
          for(const output of outputs)for(const channel of output)channel.fill(0);
          const input=inputs[0];if(!input||!input.length)return true;
          const left=input[0],right=input[1]||left;
          for(let i=0;i<left.length;i++){
            this.pcm[this.used++]=left[i];this.pcm[this.used++]=right[i];
            if(this.used===4096){this.port.postMessage(this.pcm,[this.pcm.buffer]);this.pcm=new Float32Array(4096);this.used=0;}
          }return true;
        }
      }registerProcessor('internet-pcm-tap',InternetPcmTap);`;
      let moduleUrl;
      try {
        if (!audioContext.audioWorklet) throw new Error('No AudioWorklet');
        moduleUrl = URL.createObjectURL(new Blob([workletCode], { type:'text/javascript' }));
        await audioContext.audioWorklet.addModule(moduleUrl);
        tap = new AudioWorkletNode(audioContext, 'internet-pcm-tap', {numberOfInputs:1,numberOfOutputs:1,outputChannelCount:[2]});
        ipcRenderer.send('ti-audio-backend','worklet');
        tap.port.onmessage = e => { if (spatialEnabled) ipcRenderer.send('ti-audio',e.data); };
      } catch {
        // Older/insecure pages or strict CSPs may reject worklet modules.
        tap = audioContext.createScriptProcessor(2048, 2, 2);
        ipcRenderer.send('ti-audio-backend','legacy');
        tap.onaudioprocess = e => {
        for (let c = 0; c < e.outputBuffer.numberOfChannels; c++) e.outputBuffer.getChannelData(c).fill(0);
        if (!spatialEnabled) return;
        const left = e.inputBuffer.getChannelData(0);
        const right = e.inputBuffer.getChannelData(Math.min(1, e.inputBuffer.numberOfChannels - 1));
        const pcm = new Float32Array(left.length * 2);
        for (let i = 0; i < left.length; i++) { pcm[2*i] = left[i]; pcm[2*i+1] = right[i]; }
        ipcRenderer.send('ti-audio', pcm);
        };
      } finally { if (moduleUrl) URL.revokeObjectURL(moduleUrl); }
      tap.connect(audioContext.destination);
      mediaObserver = new MutationObserver(routeMedia);
      mediaObserver.observe(document, { childList: true, subtree: true });
      document.addEventListener('loadedmetadata', routeMedia, true);
  })();
  return audioInitialization;
}
async function setSpatialAudio(enabled) {
  spatialEnabled = !!enabled;
  if (!spatialEnabled) {
    if (dryOutput) dryOutput.gain.value = 1;
    audioStatus('Normal browser audio.'); return;
  }
  try {
    await initializeAudio();
    await audioContext.resume();
    if (!spatialEnabled) { dryOutput.gain.value=1; return; }
    dryOutput.gain.value = 0;
    routeMedia();
  } catch {
    spatialEnabled = false;
    if (dryOutput) dryOutput.gain.value = 1;
    audioStatus('Audio routing unavailable; normal audio kept.');
  }
}
ipcRenderer.on('ti-spatial-audio', (e, enabled) => setSpatialAudio(enabled));

// Report a visible YouTube player failure. Recovery is bounded by the main process;
// the message contains no URL, form value, page contents or account information.
if (location.hostname === 'youtube.com' || location.hostname.endsWith('.youtube.com')) {
  let lastFailurePage = '';
  setInterval(() => {
    const error = document.querySelector('.ytp-error-content-wrap');
    if (!error || !/something went wrong|refresh or try again|an error occurred/i.test(error.textContent || '')) return;
    const rect = error.getBoundingClientRect();
    if (!rect.width || !rect.height || getComputedStyle(error).visibility === 'hidden') return;
    const page = location.href;
    if (lastFailurePage === page) return;
    lastFailurePage = page;
    ipcRenderer.send('ti-playback-error');
  }, 1500);
}
