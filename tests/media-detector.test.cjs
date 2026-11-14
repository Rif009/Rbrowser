const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('Rbrowser/Resources/Raw/media-detector.js','utf8');let checks=0;
const check=(v,m)=>{assert.ok(v,m);checks++;};
function fixture({enabled=true,resources=[],videos=[],frames=[],child=false}={}){
 const events={},sent=[];let observer;
 const location={href:'https://site.example/watch/page',hostname:'site.example'};
 const window={__rbMediaEnabled:enabled,addEventListener:(name,fn)=>events[name]=fn};window.top=child?{}:window;window.parent={postMessage:v=>sent.push(v)};
 const document={location,title:'Example episode',querySelectorAll:selector=>selector==='iframe'?frames:videos,addEventListener(){}};
 class PerformanceObserver{constructor(fn){observer=fn;}observe(){}}
 vm.runInNewContext(source,{window,document,location,URL,performance:{getEntriesByType:()=>resources.map(name=>({name}))},PerformanceObserver});
 return {api:window.__rbMedia,events,sent,push:url=>observer?.({getEntries:()=>[{name:url}]})};
}
const video=src=>({src,currentSrc:src,getAttribute:()=>null});
let f=fixture({resources:['https://cdn.example/show/master.m3u8?token=current','https://cdn.example/show/segment.ts','https://cdn.example/notvideo.js'],videos:[video('https://cdn.example/episode.mp4')]});
check(f.api.collect().length===2,'DOM and network streams found, segments and scripts excluded');
check(f.api.collect().find(v=>v.url.endsWith('.mp4')).title==='Example episode','document title accompanies media');
check(f.api.collect().find(v=>v.url.endsWith('.mp4')).preferred===true&&!f.api.collect().find(v=>v.url.includes('master.m3u8')).preferred,'current DOM video is preferred over historical requests');
check(f.api.valid('blob:https://site.example/id')===null&&f.api.valid('data:video/mp4,abc')===null,'blob and data sources not offered as native URLs');
check(f.api.valid('file:///video.mp4')===null&&f.api.valid('javascript:alert(1)')===null,'non-web schemes rejected');
f=fixture();f.push('https://cdn.example/master.m3u8?token=old');f.push('https://cdn.example/master.m3u8?token=new');check(f.api.collect().length===1&&f.api.collect()[0].url.endsWith('token=new'),'latest signed URL replaces older duplicate without storing segments');
f.events.message({data:{__rbMediaFound:{url:'episode.mp4',referrer:'https://iframe.example/player/index.html',title:'Frame title'}}});
check(f.api.collect().some(v=>v.url==='https://iframe.example/player/episode.mp4'&&v.referrer==='https://iframe.example/player/index.html'),'frame source uses its own base URL and referrer');
f.api.setEnabled(false);f.push('https://cdn.example/off.mp4');check(f.api.collect().length===0,'disabled detection clears results and ignores later network entries');
f=fixture({enabled:false});check(!f.api,'global off does not install detector');
f=fixture({child:true,videos:[video('https://cdn.example/frame.webm')]});f.api.collect();check(f.sent.some(m=>m.__rbMediaFound?.url.endsWith('.webm')),'child frame forwards detected stream to parent');
f=fixture();for(let i=0;i<80;i++)f.push('https://cdn.example/'+i+'.mp4');check(f.api.collect().length===60,'catalogue is bounded');
f.events.message({data:{__rbMediaFound:{url:'https://cdn.example/bad.mp4',title:{bad:true},referrer:{bad:true}}}});check(f.api.collect().at(-1).title==='','malformed frame metadata is normalized');
const frameDoc={location:{href:'https://site.example/frame?episode=5'},title:'Episode 5',querySelectorAll:s=>s==='iframe'?[]:[video('blob:https://site.example/current')],defaultView:{performance:{getEntriesByType:()=>[{name:'https://cdn.example/episode5.mp4'}]}}};
f=fixture({frames:[{contentDocument:frameDoc}],resources:['https://cdn.example/episode1.mp4']});
let discovered=f.api.collect();check(discovered.find(v=>v.url.endsWith('episode5.mp4')).preferred===true&&!discovered.find(v=>v.url.endsWith('episode1.mp4')).preferred,'MSE current iframe media is preferred over a previous episode');
check(discovered.find(v=>v.preferred).referrer.endsWith('episode=5'),'MSE source retains the current player frame referrer');
frameDoc.location.href='https://site.example/frame?episode=4';frameDoc.defaultView.performance.getEntriesByType=()=>[{name:'https://cdn.example/episode4.mp4'}];discovered=f.api.collect();
check(discovered.find(v=>v.url.endsWith('episode4.mp4')).preferred===true&&!discovered.find(v=>v.url.endsWith('episode5.mp4')).preferred,'switching back to a different MSE episode resets preference');
console.log(`PASS: ${checks} media detection checks`);
