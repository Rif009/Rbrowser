const assert=require('node:assert/strict');
const {createInterceptor}=require('../Rbrowser/Resources/Raw/twitch/twitch-adblock.js');
let checks=0;const check=(value,message)=>{assert.ok(value,message);checks++;};
const masterUrl='https://usher.ttvnw.net/api/channel/hls/example.m3u8?token=original&sig=original';
const variant='https://video-edge.hls.ttvnw.net/live/720.m3u8';
const originalMaster='#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=3000000,RESOLUTION=1280x720,CODECS="avc1.4d401f,mp4a.40.2"\n'+variant;
const backupMaster='#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=3000000,RESOLUTION=1280x720,CODECS="avc1.4d401f,mp4a.40.2"\nhttps://backup.hls.ttvnw.net/live/720.m3u8';
const clean='#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:100\n#EXT-X-TARGETDURATION:2\n#EXT-X-KEY:METHOD=AES-128,URI="key.bin"\n#EXTINF:2,\ncontent.ts\n';
const ad=clean.replace('#EXTINF:2,','#EXT-X-DATERANGE:ID="stitched-ad-1",CLASS="twitch-stitched-ad"\n#EXTINF:2,').replace('content.ts','advert.ts');
function fixture(mode='clean',enabled=true){
 const calls=[];let current=mode;const returned=[];
 const scope={fetch:async(input,options)=>{
  const url=typeof input==='string'?input:input.url;calls.push(url);
  if(url==='https://gql.twitch.tv/gql'){
   if(current==='delayed')await new Promise(resolve=>setTimeout(resolve,250));
   if(current==='timeout')return new Promise((resolve,reject)=>options.signal.addEventListener('abort',()=>reject(Error('aborted')),{once:true}));
   if(current==='fail')return new Response('failed',{status:503});
   const payload=JSON.parse(options.body);check(['embed','popout'].includes(payload.variables.playerType),'known backup player type');check(options.credentials==='omit','no account credentials sent');
   return Response.json({data:{streamPlaybackAccessToken:{value:'backup',signature:'backup'}}});
  }
  let response;
  if(url.startsWith('https://usher.ttvnw.net/'))response=new Response(url.includes('token=backup')?backupMaster:originalMaster);
  else if(url.startsWith('https://backup.hls.ttvnw.net/'))response=new Response(current==='incompatible'?clean.replace('SEQUENCE:100','SEQUENCE:1000'):current==='still-ad'?ad:clean);
  else response=new Response(current==='status'?ad:current==='clean'?clean:ad,{status:current==='status'?403:200,headers:{'x-test-original':'yes'}});
  returned.push(response);return response;
 }};
 const interceptor=createInterceptor(scope,{enabled,timeoutMs:150},()=>{});
 return {interceptor,calls,returned,setMode:m=>current=m};
}
(async()=>{
let f=fixture();await f.interceptor.fetch(masterUrl);const native=await f.interceptor.fetch(variant);check(native===f.returned.at(-1),'clean response object is unchanged');check(f.calls.length===2,'clean playback has no extra requests');
f.setMode('ad');const replaced=await f.interceptor.fetch(variant),text=await replaced.text();check(!text.includes('stitched-ad'),'replacement has no ad markers');check(text.includes('https://backup.hls.ttvnw.net/live/content.ts'),'segment URLs resolve against backup playlist');check(text.includes('URI="https://backup.hls.ttvnw.net/live/key.bin"'),'encryption key URL remains valid');check(f.interceptor.stats.adsDetected===1&&f.interceptor.stats.cleanReplacements===1,'real-ad path is counted');
f=fixture('fail');await f.interceptor.fetch(masterUrl);let response=await f.interceptor.fetch(variant);check(await response.clone().text()===ad,'backend failure returns original bytes');check(response.headers.get('x-test-original')==='yes','original headers retained');await f.interceptor.fetch(variant);const attempts=f.calls.filter(u=>u.includes('/gql')).length;await f.interceptor.fetch(variant);check(f.calls.filter(u=>u.includes('/gql')).length===attempts,'circuit breaker prevents repeated backend attempts');
f=fixture('timeout');await f.interceptor.fetch(masterUrl);let began=Date.now();response=await f.interceptor.fetch(variant);check(Date.now()-began<700&&await response.text()===ad,'timeout never stalls original playback');check(f.interceptor.stats.timeouts===1,'timeout is observable');
f=fixture('ad',false);await f.interceptor.fetch(masterUrl);response=await f.interceptor.fetch(variant);check(await response.text()===ad&&f.calls.length===2,'global disable makes no backup requests');
f=fixture('incompatible');await f.interceptor.fetch(masterUrl);response=await f.interceptor.fetch(variant);check(await response.text()===ad,'incompatible sequence falls back instead of skipping playback');
f=fixture('still-ad');await f.interceptor.fetch(masterUrl);response=await f.interceptor.fetch(variant);check(await response.text()===ad,'ad-bearing backup is never substituted');
f=fixture('ad');response=await f.interceptor.fetch(variant);check(await response.text()===ad&&f.calls.length===1,'unknown rendition preserved');
f=fixture('status');response=await f.interceptor.fetch(variant);check(response.status===403&&f.calls.length===1,'original non-success status preserved');
f=fixture('ad');await f.interceptor.fetch(masterUrl);f.interceptor.setEnabled(false);response=await f.interceptor.fetch(variant);check(await response.text()===ad&&f.calls.length===2,'runtime disable preserves stream');
f=fixture('clean');await f.interceptor.fetch(masterUrl);f.interceptor.probeNext();await f.interceptor.fetch(variant);check(f.interceptor.stats.simulatedAds===1&&f.interceptor.stats.adsDetected===0,'diagnostic simulation is distinct from real ad evidence');
f=fixture('delayed');await f.interceptor.fetch(masterUrl);response=await f.interceptor.fetch(variant);check(await response.text()===ad,'slow first acquisition leaves original response intact');await new Promise(resolve=>setTimeout(resolve,300));const tokenCalls=f.calls.filter(u=>u.includes('/gql')).length;response=await f.interceptor.fetch(variant);check(!(await response.text()).includes('stitched-ad'),'later playlist uses warmed clean rendition');check(f.calls.filter(u=>u.includes('/gql')).length===tokenCalls,'warm cache avoids repeated token requests');
console.log(`PASS: ${checks} Twitch HLS checks`);
})().catch(e=>{console.error(e);process.exitCode=1;});
