(() => {
 if (window.__rb) return;
 const style = document.createElement('style');
 style.textContent = '[data-rb-focus]{outline:3px solid #a2c8d5!important;outline-offset:4px!important;box-shadow:0 0 0 6px #17252dcc!important}';
 (document.head || document.documentElement).append(style);
 let selected = null;
 let cinemaTargets=[];
 const visible = el => { const r = el.getBoundingClientRect(), s = getComputedStyle(el); return r.width > 3 && r.height > 3 && s.visibility !== 'hidden' && s.display !== 'none' && !el.disabled; };
 function choose(el) { if (selected) selected.removeAttribute('data-rb-focus'); selected = el; el.setAttribute('data-rb-focus',''); if(!el.hasAttribute('tabindex'))el.setAttribute('tabindex','0');try { el.focus({preventScroll:true}); } catch {} el.scrollIntoView({block:'nearest',inline:'nearest',behavior:'instant'}); }
 window.__rb = {
  move(direction) {
   const nodes = [...document.querySelectorAll('a[href],button,input:not([type=hidden]),select,textarea,video,[role=button],[role=link],[role=tab],[onclick]:not(body),[tabindex]')].filter(visible);
   if (!selected || !document.contains(selected) || !visible(selected)) {
    const first = nodes.find(el => {const r=el.getBoundingClientRect();return r.bottom>0 && r.top<innerHeight && r.right>0 && r.left<innerWidth;});
    if (first) {choose(first); return 'focus';}
   }
   const r = selected?.getBoundingClientRect(); let best=null, score=Infinity;
   if(r) for(const el of nodes) {
    if(el===selected || selected.contains(el) || el.contains(selected))continue;
    const b=el.getBoundingClientRect(), dx=b.x+b.width/2-r.x-r.width/2, dy=b.y+b.height/2-r.y-r.height/2;
    const primary=direction==='left'?-dx:direction==='right'?dx:direction==='up'?-dy:dy;
    const secondary=(direction==='left'||direction==='right')?Math.abs(dy):Math.abs(dx);
    if(primary<3)continue;
    const rank=primary+secondary*2.5;
    if(rank<score){score=rank;best=el;}
   }
   if(best){choose(best);return 'focus';}
   window.scrollBy({left:direction==='left'?-innerWidth*.65:direction==='right'?innerWidth*.65:0,top:direction==='up'?-innerHeight*.65:direction==='down'?innerHeight*.65:0,behavior:'instant'});
   if(selected){selected.removeAttribute('data-rb-focus');selected=null;}return 'scroll';
  },
  activate() {
   if(document.activeElement && document.activeElement!==document.body && visible(document.activeElement))selected=document.activeElement;
   if(!selected)return 'none';
   if(selected.tagName==='IFRAME'){selected.focus();return 'frame';}
   if(selected.matches('input,textarea,select')){selected.focus();selected.click();return 'input';}
   if(selected.tagName==='VIDEO'){selected.paused?selected.play().catch(()=>{}):selected.pause();return 'video';}
   selected.click();return 'click';
  },
  target() {
   if(document.activeElement && document.activeElement!==document.body && visible(document.activeElement))selected=document.activeElement;
   if(!selected||!document.contains(selected)||!visible(selected))return null;
   if(!selected.matches('input,textarea,select,[contenteditable=true]')&&selected.tagName!=='IFRAME'&&selected.tagName!=='VIDEO'){
    const control=selected.closest('button,a[href],[role=button],[role=link],[role=tab],[onclick]');
    if(control&&control!==document.body){try{document.activeElement?.blur();}catch{}control.click();return {kind:'handled'};}
   }
   const r=selected.getBoundingClientRect(),vp=window.visualViewport;
   const width=vp?.width||innerWidth,height=vp?.height||innerHeight;
   const x=(Math.max(0,r.left)+Math.min(width,r.right))/2;
   const y=(Math.max(0,r.top)+Math.min(height,r.bottom))/2;
   return {kind:selected.matches('input,textarea,select,[contenteditable=true]')?'input':selected.tagName==='IFRAME'?'frame':selected.tagName==='VIDEO'?'video':'tap',x,y,width,height};
  },
  pointerTarget(fx,fy) {
   const vp=window.visualViewport,x=fx*(vp?.width||innerWidth)+(vp?.offsetLeft||0),y=fy*(vp?.height||innerHeight)+(vp?.offsetTop||0);
   const hit=document.elementFromPoint(x,y);if(!hit)return {kind:'tap'};
   const editable=hit.closest('input,textarea,select,[contenteditable=true]');
   if(!editable)try{document.activeElement?.blur();}catch{}
   if(editable){try{editable.focus({preventScroll:true});}catch{}return {kind:'input'};}
   const control=hit.closest('button,[role=button],[role=link],[role=tab],[onclick]');
   if(control&&control!==document.body){control.click();return {kind:'handled'};}
   const anchor=hit.closest('a[href]');if(anchor)try{const u=new URL(anchor.href,location.href);if(/^https?:$/.test(u.protocol)&&!anchor.hasAttribute('onclick')&&u.href.split('#')[0]!==location.href.split('#')[0])return {kind:'link',url:u.href};anchor.click();return {kind:'handled'};}catch{}
   return {kind:'tap'};
  },
  video(command) {
   const frames=[...document.querySelectorAll('iframe')];
   const videos=[...document.querySelectorAll('video')];
   for(const frame of frames)try{videos.push(...frame.contentDocument.querySelectorAll('video'));}catch{}
   const v=videos.find(v=>!v.paused)||videos.find(visible);if(!v)return false;
   if(command==='toggle')v.paused?v.play().catch(()=>{}):v.pause();
   if(command==='left'&&Number.isFinite(v.duration))v.currentTime=Math.max(0,v.currentTime-10);
   if(command==='right'&&Number.isFinite(v.duration))v.currentTime=Math.min(v.duration,v.currentTime+10);
   return true;
  },
  cinemaExit() {
   for(const {doc,target,ancestors,cleanup} of cinemaTargets){cleanup?.();doc.documentElement.classList.remove('rb-cinema');target.classList.remove('rb-cinema-target');for(const a of ancestors)a.classList.remove('rb-cinema-ancestor');}
   cinemaTargets=[];return false;
  },
  cinemaToggle() {
   if(cinemaTargets.length)return this.cinemaExit();
   const entries=[...document.querySelectorAll('video')].map(v=>({v,frame:null}));
   for(const frame of document.querySelectorAll('iframe'))try{for(const v of frame.contentDocument.querySelectorAll('video'))entries.push({v,frame});}catch{}
   const entry=entries.find(e=>!e.v.paused)||entries.find(e=>visible(e.v));if(!entry)return false;
   const pin=(doc,target)=>{
    let css=doc.getElementById('rb-cinema-css');if(!css){css=doc.createElement('style');css.id='rb-cinema-css';css.textContent='html.rb-cinema{overflow:hidden!important}html.rb-cinema body *{visibility:hidden!important}html.rb-cinema .rb-cinema-ancestor{transform:none!important;contain:none!important;overflow:visible!important;position:static!important}html.rb-cinema .rb-cinema-target,html.rb-cinema .rb-cinema-target *{visibility:visible!important}html.rb-cinema .rb-cinema-target{position:fixed!important;inset:0!important;margin:0!important;padding:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;z-index:2147483647!important;object-fit:contain!important;background:#000!important;border:0!important}';(doc.head||doc.documentElement).append(css);}
    // Overview scaling can make the visible viewport larger than CSS 100vw/100vh on TV.
    const win=doc.defaultView,viewport=win?.visualViewport;
    const fit=()=>{const scale=viewport?.width&&doc.documentElement.clientWidth?viewport.width/doc.documentElement.clientWidth:1;css.textContent=css.textContent.replace(/width:(?:100vw|calc\(100vw \* [\d.]+\))!important;height:(?:100vh|calc\(100vh \* [\d.]+\))!important/,`width:calc(100vw * ${scale})!important;height:calc(100vh * ${scale})!important`);};
    fit();viewport?.addEventListener?.('resize',fit);win?.addEventListener?.('resize',fit);
    const cleanup=()=>{viewport?.removeEventListener?.('resize',fit);win?.removeEventListener?.('resize',fit);};
    const ancestors=[];for(let a=target.parentElement;a&&a!==doc.documentElement;a=a.parentElement){a.classList.add('rb-cinema-ancestor');ancestors.push(a);}
    target.classList.add('rb-cinema-target');doc.documentElement.classList.add('rb-cinema');cinemaTargets.push({doc,target,ancestors,cleanup});
   };
   if(entry.frame){pin(document,entry.frame);pin(entry.v.ownerDocument,entry.v);}else pin(document,entry.v);
   return true;
  },
  pause(){document.querySelectorAll('video,audio').forEach(v=>v.pause());document.querySelectorAll('iframe').forEach(f=>f.contentWindow?.postMessage({__rb_media:'pause'},'*'));},
  css(text){let s=document.getElementById('rb-filter-css');if(!s){s=document.createElement('style');s.id='rb-filter-css';(document.head||document.documentElement).append(s);}s.textContent=text;}
 };
 // Include embedded players as reachable D-pad destinations. Cross-origin content uses native keys.
 const move=window.__rb.move;
 window.__rb.move=function(d){document.querySelectorAll('iframe').forEach(f=>{if(!f.hasAttribute('tabindex'))f.setAttribute('tabindex','0');});return move(d);};
 window.addEventListener('pagehide',()=>window.__rb.cinemaExit());
})();
