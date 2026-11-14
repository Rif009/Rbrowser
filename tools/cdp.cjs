// Local Android WebView diagnostics through an explicitly forwarded ADB socket.
const fs=require('node:fs');
(async()=>{
const targets=await(await fetch('http://127.0.0.1:9222/json')).json();
const needle=process.argv.find(x=>x.startsWith('--url='))?.slice(6)||'';
const target=targets.find(t=>t.type==='page'&&t.url.includes(needle)&&JSON.parse(t.description||'{}').attached&&JSON.parse(t.description||'{}').visible)||targets.find(t=>t.type==='page'&&t.url.includes(needle)&&JSON.parse(t.description||'{}').visible)||targets.find(t=>t.type==='page'&&t.url.includes(needle));
if(!target)throw Error('No matching WebView');
const socket=new WebSocket(target.webSocketDebuggerUrl);const pending=new Map();let id=0;const contexts=[];const requests=[];
socket.addEventListener('message',e=>{const data=JSON.parse(e.data);if(data.method==='Network.requestWillBeSent'){const url=new URL(data.params.request.url);requests.push(process.argv[2]==='network-summary'?{type:data.params.type,host:url.hostname,path:url.pathname,queryKeys:[...url.searchParams.keys()]}:{type:data.params.type,url:url.href});}if(data.method==='Runtime.executionContextCreated')contexts.push(data.params.context);if(data.id){const item=pending.get(data.id);if(item){pending.delete(data.id);data.error?item.reject(Error(JSON.stringify(data.error))):item.resolve(data.result);}}});
await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
const call=(method,params={})=>new Promise((resolve,reject)=>{const key=++id;const timer=setTimeout(()=>{pending.delete(key);reject(Error('CDP timeout '+method));},15000);pending.set(key,{resolve:x=>{clearTimeout(timer);resolve(x);},reject:e=>{clearTimeout(timer);reject(e);}});socket.send(JSON.stringify({id:key,method,params}));});
try{
await call('Runtime.enable');
if(['network','network-summary'].includes(process.argv[2])){await call('Network.enable');await call('Page.reload',{ignoreCache:true});await new Promise(r=>setTimeout(r,process.argv[2]==='network-summary'?16000:6000));console.log(JSON.stringify(requests,null,2));}
else if(process.argv[2]==='frames'){console.log(JSON.stringify({target:target.url,frames:await call('Page.getFrameTree'),contexts},null,2));}
else{
const expression=fs.readFileSync(process.argv[3],'utf8');const context=process.argv.find(x=>x.startsWith('--context='))?.slice(10);
const result=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,timeout:10000,...(context?{contextId:Number(context)}:{})});console.log(JSON.stringify(result,null,2));
}
}finally{socket.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
