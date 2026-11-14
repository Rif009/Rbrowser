const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function element(tag,x,y,width=100,height=40) {
 const attributes = new Map();
 const classes=new Set();return {tagName:tag,disabled:false,attrs:attributes,clicked:0,focused:false,scrolled:false,classList:{add:n=>classes.add(n),remove:n=>classes.delete(n),contains:n=>classes.has(n)},
  getBoundingClientRect:()=>({x,y,width,height,top:y,bottom:y+height,left:x,right:x+width}),
  setAttribute:(k,v)=>attributes.set(k,v),removeAttribute:k=>attributes.delete(k),hasAttribute:k=>attributes.has(k),
  focus(){this.focused=true;},click(){this.clicked++;},scrollIntoView(){this.scrolled=true;},contains:()=>false,
  closest(){return this.tagName==='BUTTON'?this:null;},
  matches:s=>tag==='INPUT'&&s.includes('input')};
}
const left=element('A',10,10),right=element('BUTTON',150,10),lower=element('INPUT',150,100),frame=element('IFRAME',300,100);
let nodes=[left,right,lower,frame];let scrolls=[],videos=[];
let cinemaStyle,resize;
const document={defaultView:{visualViewport:{width:1010,height:568.125,addEventListener(type,fn){resize=fn;},removeEventListener(){resize=null;}}},documentElement:element('HTML',0,0,900,500),head:{append(s){cinemaStyle=s;}},createElement:()=>({}),contains:el=>nodes.includes(el),
 querySelectorAll:s=>s==='iframe'?[frame]:s==='video'||s==='video,audio'?videos:nodes,getElementById:()=>null};
const window={scrollBy:opts=>scrolls.push(opts),addEventListener(){}};
vm.runInNewContext(fs.readFileSync('Rbrowser/Resources/Raw/navigation.js','utf8'),{window,document,URL,location:{href:'https://v14.vost.pw/'},innerWidth:900,innerHeight:500,getComputedStyle:()=>({visibility:'visible',display:'block'})});
let count=0;function check(value,message){assert.ok(value,message);count++;}
check(window.__rb.move('right')==='focus'&&left.focused,'initial selection is visible');
check(window.__rb.move('right')==='focus'&&right.attrs.has('data-rb-focus')&&!left.attrs.has('data-rb-focus'),'move right and clear old focus');
check(window.__rb.activate()==='click'&&right.clicked===1,'OK activates selected button');
check(window.__rb.move('down')==='focus'&&lower.attrs.has('data-rb-focus'),'directional geometry selects input below');
check(window.__rb.activate()==='input','inputs request keyboard mode');
check(window.__rb.target().kind==='input','only an actual editable target requests native text focus');
check(window.__rb.move('right')==='focus'&&frame.attrs.has('data-rb-focus'),'iframe is a reachable destination');
check(window.__rb.activate()==='frame','embedded player delegates to native keys');
const target=window.__rb.target();check(target.kind==='frame'&&target.x===350&&target.y===120,'native activation receives focused element coordinates');
nodes=[];
check(window.__rb.move('down')==='scroll'&&scrolls.at(-1).top>0,'empty direction scrolls page');
check(window.__rb.video('toggle')===false,'missing video returns false');
const video=element('VIDEO',0,0,640,360);video.ownerDocument=document;video.paused=false;video.currentTime=15;video.duration=120;video.pause=()=>video.paused=true;video.play=()=>{video.paused=false;return Promise.resolve();};nodes=[video];videos=[video];
check(window.__rb.video('left')&&video.currentTime===5,'remote seeks backward by ten seconds');
check(window.__rb.video('right')&&video.currentTime===15,'remote seeks forward by ten seconds');
check(window.__rb.video('toggle')&&video.paused,'remote pause');
check(window.__rb.video('toggle')&&!video.paused,'remote resume');
document.documentElement.clientWidth=980;
check(window.__rb.cinemaToggle()&&document.documentElement.classList.contains('rb-cinema')&&video.classList.contains('rb-cinema-target'),'fullscreen isolates current video');
check(cinemaStyle.textContent.includes('width:calc(100vw * '+(1010/980)+')!important;height:calc(100vh * '+(1010/980)+')!important'),'fullscreen compensates overview scaling while retaining responsive viewport units');
document.defaultView.visualViewport.width=1960;resize();check(cinemaStyle.textContent.includes('height:calc(100vh * 2)!important'),'fullscreen follows overview scale changes without freezing native viewport height');
check(window.__rb.cinemaExit()===false&&!document.documentElement.classList.contains('rb-cinema')&&!video.classList.contains('rb-cinema-target'),'leaving fullscreen restores page');
check(resize===null,'leaving fullscreen removes viewport listener');
let hitPoint,blurred=0;document.activeElement={blur(){blurred++;}};window.visualViewport={width:1010,height:568,offsetLeft:30,offsetTop:20};
document.elementFromPoint=(x,y)=>{hitPoint={x,y};return {closest:s=>s==='a[href]'?{href:'https://v14.vost.pw/tip/tv/example.html',hasAttribute:()=>false}:null};};
let pointed=window.__rb.pointerTarget(.5,.5);check(pointed.kind==='link'&&pointed.url.includes('/tip/tv/'),'cursor on image inside anchor resolves actual navigation target');
check(hitPoint.x===535&&hitPoint.y===304,'cursor hit test accounts for overview size and viewport offset');check(blurred===1,'clicking a link blurs stale search input');
document.elementFromPoint=()=>({closest:s=>s.startsWith('input')?{}:null});pointed=window.__rb.pointerTarget(.2,.3);check(pointed.kind==='input'&&blurred===1,'real input click remains editable without forced blur');
let scriptClicks=0;document.elementFromPoint=()=>({closest:s=>s==='a[href]'?{href:'javascript:void(0)',click(){scriptClicks++;}}:null});check(window.__rb.pointerTarget(.2,.3).kind==='handled'&&scriptClicks===1,'script link executes its DOM action without a native focus / touch jump');
let episodeClicks=0;const episode={click(){episodeClicks++;}};document.elementFromPoint=()=>({closest:s=>s.includes('[onclick]')?episode:null});
check(window.__rb.pointerTarget(.75,.5).kind==='handled'&&episodeClicks===1,'Vost onclick series button activates directly without native focus');
document.elementFromPoint=()=>({closest:()=>null});check(window.__rb.pointerTarget(.75,.5).kind==='tap','plain areas and video retain native touch fallback');
nodes=[right];document.activeElement=right;check(window.__rb.target().kind==='handled'&&right.clicked===2,'D-pad button activation uses DOM action without stale input refocus');
let hashClicks=0;document.activeElement=null;document.elementFromPoint=()=>({closest:s=>s==='a[href]'?{href:'https://v14.vost.pw/#episode5',hasAttribute:()=>false,click(){hashClicks++;}}:null});
check(window.__rb.pointerTarget(.75,.5).kind==='handled'&&hashClicks===1,'same-document link preserves site handler and avoids full reload');
console.log(`PASS: ${count} remote navigation checks`);
