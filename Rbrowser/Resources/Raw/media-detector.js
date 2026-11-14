(() => {
 if(window.__rbMedia || window.__rbMediaEnabled===false)return;
 let enabled=true;const found=new Map();
 const valid=(value,base=location.href)=>{try{if(typeof value!=='string'||value.length>32768)return null;const u=new URL(value,base);return /^https?:$/.test(u.protocol)&&/\.(m3u8|mpd|mp4|m4v|webm)$/i.test(u.pathname)?u.href:null;}catch{return null;}};
 function add(value,referrer=location.href,title='',preferred=false){
  if(!enabled)return;referrer=typeof referrer==='string'?referrer:location.href;title=typeof title==='string'?title.slice(0,160):'';const url=valid(value,referrer);if(!url)return;
  const u=new URL(url),key=u.origin+u.pathname,previous=found.get(key),item={url,referrer,title:title||previous?.title||'',preferred:!!preferred||!!previous?.preferred};found.delete(key);found.set(key,item);if(found.size>60)found.delete(found.keys().next().value);
  if(window!==window.top)window.parent.postMessage({__rbMediaFound:item},'*');
 }
 function scan(doc=document){
  try{const sources=[...doc.querySelectorAll('video,video source')].flatMap(v=>[v.currentSrc,v.src,v.getAttribute('src'),v.getAttribute('data-src')].filter(Boolean));sources.forEach(src=>add(src,doc.location.href,doc.title,true));
   // A blob/MSE player's direct media requests belong to its current document, not an obsolete episode iframe.
   if(!sources.some(src=>valid(src,doc.location.href))&&sources.some(src=>src.startsWith('blob:')))try{doc.defaultView.performance.getEntriesByType('resource').forEach(e=>add(e.name,doc.location.href,doc.title,true));}catch{}
   doc.querySelectorAll('iframe').forEach(f=>{try{if(f.contentDocument)scan(f.contentDocument);f.contentWindow?.postMessage({__rbMediaScan:true},'*');}catch{}});
  }catch{}
 }
 window.addEventListener('message',e=>{if(!enabled)return;if(e.data?.__rbMediaFound){const m=e.data.__rbMediaFound;add(m.url,m.referrer,m.title,m.preferred===true);}if(e.data?.__rbMediaScan)scan();});
 document.addEventListener('loadedmetadata',()=>scan(),true);document.addEventListener('play',()=>scan(),true);
 document.addEventListener('DOMContentLoaded',()=>scan(),{once:true});
 try{new PerformanceObserver(list=>list.getEntries().forEach(e=>add(e.name))).observe({type:'resource',buffered:true});}catch{}
 window.__rbMedia={collect(){if(!enabled)return [];for(const item of found.values())item.preferred=false;scan();try{performance.getEntriesByType('resource').forEach(e=>add(e.name));}catch{}return [...found.values()];},setEnabled(value){enabled=!!value;if(!enabled)found.clear();},valid};
})();
