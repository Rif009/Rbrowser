/* Rbrowser Twitch HLS protection. No segment stripping, external proxy, player reload or pause override.
 * Clean-rendition acquisition follows the documented TwitchAdSolutions approach (MIT), with bounded
 * attempts and original-response fallback. All playback-token requests go to Twitch's own API.
 */
(function () {
 'use strict';
 function createInterceptor(scope, config, notify) {
  const originalFetch=scope.fetch.bind(scope), masters=new Map(), cached=new Map(), pending=new Map(), failures=new Map();
  const stats={playlists:0,adsDetected:0,cleanReplacements:0,failOpen:0,timeouts:0,simulatedAds:0,backupTokens:0,backupMasters:0,backupMedia:0,incompatible:0};
  let enabled=config.enabled!==false, simulate=false;
  const budget=Math.max(100,Math.min(config.timeoutMs||1800,2500));
  const report=()=>{try{notify?.({...stats});}catch{}};
  const ad=text=>/(?:stitched-ad|#EXT-X-CUE-OUT|SCTE35-OUT)/i.test(text);
  const key=url=>{const u=new URL(url);return u.origin+u.pathname;};
  const sequence=text=>Number(text.match(/#EXT-X-MEDIA-SEQUENCE:(\d+)/)?.[1]||0);
  const compatible=(a,b)=>Math.abs(sequence(a)-sequence(b))<=2;
  function variants(text,base) {
   const lines=text.replaceAll('\r','').split('\n'),result=[];
   for(let i=0;i<lines.length-1;i++)if(lines[i].startsWith('#EXT-X-STREAM-INF:')){
    const attributes=Object.fromEntries([...lines[i].matchAll(/([\w-]+)=("[^"]*"|[^,]*)/g)].map(m=>[m[1],m[2].replace(/^"|"$/g,'')]));
    const [width,height]=(attributes.RESOLUTION||'0x0').split('x').map(Number);const path=lines[i+1].trim();
    if(path&&!path.startsWith('#'))result.push({url:new URL(path,base).href,width,height,codec:attributes.CODECS||'',fps:Number(attributes['FRAME-RATE']||0)});
   }
   return result;
  }
  function observeMaster(url,text) {
   const channel=new URL(url).pathname.match(/\/channel\/hls\/([^/]+)\.m3u8$/)?.[1];if(!channel)return;
   for(const v of variants(text,url)){masters.set(key(v.url),{...v,channel,masterUrl:url});if(masters.size>256)masters.delete(masters.keys().next().value);}
  }
  function absolute(text,base) {
   return text.split('\n').map(line=>{if(line.trim()&&!line.startsWith('#'))return new URL(line.trim(),base).href;return line.replace(/URI="([^"]+)"/g,(_,path)=>'URI="'+new URL(path,base).href+'"');}).join('\n');
  }
  const valid=text=>text.startsWith('#EXTM3U')&&text.includes('#EXTINF:')&&!ad(text)&&text.split('\n').some(s=>s.trim()&&!s.startsWith('#'));
  async function read(url,options) {const response=await originalFetch(url,options);if(!response.ok)throw Error('Upstream response');return response.text();}
  async function backup(meta,type,signal,original) {
   const cacheKey=meta.channel+'|'+meta.width+'x'+meta.height+'|'+type;
   if(cached.has(cacheKey))try{const url=cached.get(cacheKey);const text=await read(url,{signal});if(valid(text)&&compatible(original,text))return absolute(text,url);}catch{}
   cached.delete(cacheKey);
   const tokenResponse=await originalFetch('https://gql.twitch.tv/gql',{method:'POST',credentials:'omit',signal,headers:{'Client-ID':'kimne78kx3ncx6brgo4mv6wki5h1ko','Content-Type':'application/json'},body:JSON.stringify({operationName:'RbrowserPlaybackToken',query:'query RbrowserPlaybackToken($login: String!, $playerType: String!) { streamPlaybackAccessToken(channelName: $login, params: {platform: "web", playerBackend: "mediaplayer", playerType: $playerType}) { value signature } }',variables:{login:meta.channel,playerType:type}})});
   if(!tokenResponse.ok)throw Error('Token unavailable');const token=(await tokenResponse.json()).data?.streamPlaybackAccessToken;if(!token?.value||!token.signature)throw Error('Token unavailable');stats.backupTokens++;
   // The primary player's session/query parameters need not be valid for an anonymous embed token.
   const master=new URL('https://usher.ttvnw.net/api/channel/hls/'+encodeURIComponent(meta.channel)+'.m3u8');
   master.search=new URLSearchParams({token:token.value,sig:token.signature,allow_source:'true',allow_audio_only:'true'});
   const masterText=await read(master.href,{signal}), choices=variants(masterText,master.href).filter(v=>!meta.codec||!v.codec||v.codec.split(',')[0].split('.')[0]===meta.codec.split(',')[0].split('.')[0]);stats.backupMasters++;
   choices.sort((a,b)=>Math.abs(a.width*a.height-meta.width*meta.height)-Math.abs(b.width*b.height-meta.width*meta.height)||Math.abs(a.fps-meta.fps)-Math.abs(b.fps-meta.fps));
   if(!choices.length)throw Error('No compatible rendition');const url=choices[0].url,text=await read(url,{signal});
   stats.backupMedia++;if(!valid(text))throw Error('No clean playlist');cached.set(cacheKey,url);
   if(!compatible(original,text)){stats.incompatible++;throw Error('No compatible playlist');}return absolute(text,url);
  }
  async function acquire(meta,original) {
   const id=meta.channel+'|'+meta.width+'x'+meta.height;if(pending.has(id))return pending.get(id);
   const controller=new AbortController();let timer;
   const task=(async()=>{try{
    return await Promise.race([Promise.any(['embed','popout'].map(type=>backup(meta,type,controller.signal,original))).catch(()=>null),new Promise(resolve=>{timer=setTimeout(()=>resolve(null),Math.max(budget,Math.min(config.backgroundTimeoutMs||6000,6000)));})]);
   }finally{clearTimeout(timer);controller.abort();pending.delete(id);report();}})();
   pending.set(id,task);return task;
  }
  async function processPlaylist(url,text) {
   if(!enabled)return text;stats.playlists++;
   const natural=ad(text),forced=simulate;simulate=false;
   if(!natural&&!forced){failures.delete(key(url));report();return text;}
   if(natural)stats.adsDetected++;if(forced)stats.simulatedAds++;
   const meta=masters.get(key(url)), failure=failures.get(key(url));
   if(!meta||(failure?.until||0)>Date.now()){stats.failOpen++;report();return text;}
   let deadline;
   try{
    // Return the original on deadline; allow the bounded in-flight acquisition to populate the URL cache.
    // A slow first request must not prevent later ad playlists from using the warmed rendition.
    const clean=await Promise.race([acquire(meta,text),new Promise(resolve=>{deadline=setTimeout(()=>{stats.timeouts++;resolve(null);},budget);})]);
    if(enabled&&clean&&valid(clean)&&compatible(text,clean)){stats.cleanReplacements++;failures.delete(key(url));report();return clean;}
   }catch{}finally{clearTimeout(deadline);}
   if(!pending.has(meta.channel+'|'+meta.width+'x'+meta.height)){const count=(failure?.count||0)+1;failures.set(key(url),{count,until:count>=2?Date.now()+30000:0});}stats.failOpen++;report();return text;
  }
  async function hookedFetch(input,init) {
   const response=await originalFetch(input,init);if(!enabled||response.status!==200)return response;
   const url=typeof input==='string'?input:input?.url;if(!url)return response;
   try{
    const parsed=new URL(url);if(!parsed.pathname.endsWith('.m3u8')||!/(^|\.)(ttvnw\.net|twitch\.tv)$/.test(parsed.hostname))return response;
    const text=await response.clone().text();if(!text.startsWith('#EXTM3U'))return response;
    if(text.includes('#EXT-X-STREAM-INF:')){observeMaster(url,text);return response;}
    const processed=await processPlaylist(url,text);if(processed===text)return response;
    const headers=new Headers(response.headers);headers.delete('content-length');headers.delete('content-encoding');headers.set('content-type','application/vnd.apple.mpegurl');
    return new Response(processed,{status:response.status,statusText:response.statusText,headers});
   }catch{return response;}
  }
  return {stats,ad,variants,compatible,absolute,observeMaster,processPlaylist,fetch:hookedFetch,install(){scope.fetch=hookedFetch;},setEnabled(value){enabled=!!value;},probeNext(){simulate=true;}};
 }
 if(typeof module!=='undefined'&&module.exports){module.exports={createInterceptor};return;}
 if(typeof window==='undefined'||!/(^|\.)twitch\.tv$/.test(location.hostname)||window.__rbTwitch)return;
 const config=window.__rbTwitchConfig||{enabled:true};
 config.enabled=config.enabled!==false&&!(config.excludedHosts||[]).includes(location.hostname);
 if(!config.enabled){window.__rbTwitch={enabled:false,stats:{workersAttached:0},setEnabled(){}};return;}
 const workers=new Map(),mainStats={};let constructionFallbacks=0;
 const interceptor=createInterceptor(window,config,s=>Object.assign(mainStats,s));
 const controller={get enabled(){return config.enabled;},get stats(){const total={...mainStats,workersAttached:workers.size,constructionFallbacks};for(const s of workers.values())for(const [key,value]of Object.entries(s))total[key]=(total[key]||0)+value;return total;},setEnabled(value){config.enabled=!!value;interceptor.setEnabled(value);for(const worker of workers.keys())worker.postMessage({__rbTwitchControl:'enabled',value:!!value});}};
 if(config.diagnostics)controller.probeNext=()=>{interceptor.probeNext();for(const worker of workers.keys())worker.postMessage({__rbTwitchControl:'probe'});};
 window.__rbTwitch=controller;interceptor.install();
 const NativeWorker=window.Worker;
 if(NativeWorker)window.Worker=new Proxy(NativeWorker,{construct(Target,args){
  const [sourceUrl,options]=args;let objectUrl;
  try{
   const url=new URL(String(sourceUrl),location.href);if(url.protocol!=='blob:'||url.origin!==location.origin)return Reflect.construct(Target,args);
   const xhr=new XMLHttpRequest();xhr.open('GET',url.href,false);xhr.send();if(!xhr.responseText||xhr.responseText.length>10000000)return Reflect.construct(Target,args);
   const prefix='"use strict";const __rbTwitchInterceptor=('+createInterceptor.toString()+')(globalThis,'+JSON.stringify(config)+',s=>postMessage({__rbTwitchStats:s}));__rbTwitchInterceptor.install();addEventListener("message",e=>{if(e.data?.__rbTwitchControl==="enabled"){e.stopImmediatePropagation();__rbTwitchInterceptor.setEnabled(e.data.value);}if(e.data?.__rbTwitchControl==="probe"){e.stopImmediatePropagation();__rbTwitchInterceptor.probeNext();}});\n';
   objectUrl=URL.createObjectURL(new Blob([prefix,xhr.responseText],{type:'text/javascript'}));const worker=new Target(objectUrl,options);workers.set(worker,{});
   worker.addEventListener('message',e=>{if(e.data?.__rbTwitchStats){e.stopImmediatePropagation();workers.set(worker,e.data.__rbTwitchStats);}});
   const terminate=worker.terminate.bind(worker);worker.terminate=()=>{workers.delete(worker);terminate();};setTimeout(()=>URL.revokeObjectURL(objectUrl),1000);return worker;
  }catch{constructionFallbacks++;if(objectUrl)URL.revokeObjectURL(objectUrl);return Reflect.construct(Target,args);}
 }});
})();
