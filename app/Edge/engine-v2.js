let readyResolve,readyReject,words,corpus,shorthand,singles;
const keepShortEnglish=new Set(['a','i','he','me','be','am','an','as','at','by','do','go','if','in','is','it','my','no','of','on','or','so','to','up','we','us','you','ye','hen','ai']);
const ready=new Promise((ok,no)=>{readyResolve=ok;readyReject=no});
const assets=globalThis.QJ_NODE_ASSETS;
var Module={...(assets?{wasmBinary:assets.wasm,getPreloadedPackage:()=>assets.data}:{}),locateFile:n=>assets?assets.base+'/engine/'+n:'engine/'+n,onAbort:readyReject,print:()=>{},printErr:()=>{},onRuntimeInitialized:async()=>{
 try{
  Module.FS.mkdir('/rime');
  for(const n of ['pinyin_simp.prism.bin','pinyin_simp.reverse.bin','pinyin_simp.schema.yaml','pinyin_simp.table.bin']){
   const r=await fetch('engine/'+n);if(!r.ok)throw Error('词库缺失');let bytes=new Uint8Array(await r.arrayBuffer());
   if(n.endsWith('.yaml'))bytes=new TextEncoder().encode(new TextDecoder().decode(bytes).replace('translator:\n','translator:\n  enable_user_dict: false\n'));
   Module.FS.writeFile('/usr/share/rime-data/build/'+n,bytes);
  }
  Module.ccall('init',null,[],[]);Module.ccall('set_ime',null,['string'],['pinyin_simp']);Module.ccall('set_page_size',null,['number'],[6]);Module.ccall('set_option',null,['string','number'],['ascii_mode',0]);
  words=new Set(await(await fetch('engine/english.json')).json());corpus=await(await fetch('engine/corpus.json')).json();shorthand=await(await fetch('shorthand.json')).json();singles=await(await fetch('single-syllable.json')).json();readyResolve();
 }catch(e){readyReject(e)}
}};
importScripts('engine/rime.js');
function keys(s){return JSON.parse(Module.ccall('process','string',['string'],[s]))}
function select(i){return JSON.parse(Module.ccall('select_candidate_on_current_page','string',['number'],[i]))}
function query(s){keys('{Escape}');return keys(s)}
function completeCandidate(input,index){
 query(input);let r=select(index),text=r.committed||'',guard=0;
 while(r.state===1&&guard++<20){r=keys('{space}');text+=r.committed||''}
 return r.state!==1&&/^[\u3400-\u9fff]{1,80}$/.test(text)?text:null;
}
// Bounded, common Rime-style fuzzy pairs, applied at syllable boundaries.
function fuzzyVariants(syllables){
 const out=[];
 for(let i=0;i<syllables.length;i++){
  const s=syllables[i],alternatives=[];
  for(const [a,b]of [['z','zh'],['c','ch'],['s','sh'],['n','l']]){
   if(s.startsWith(b))alternatives.push(a+s.slice(b.length));else if(s.startsWith(a)&&!s.startsWith(a+'h'))alternatives.push(b+s.slice(a.length));
  }
  for(const [a,b]of [['an','ang'],['en','eng'],['in','ing']]){
   if(s.endsWith(b))alternatives.push(s.slice(0,-b.length)+a);else if(s.endsWith(a))alternatives.push(s.slice(0,-a.length)+b);
  }
  for(const alt of new Set(alternatives)){const v=[...syllables];v[i]=alt;out.push(v.join("'"));if(out.length>=6)return out}
 }
 return out;
}
onmessage=async({data:{id,text,force,fuzzy=true}})=>{
 try{
  await ready;
  if(typeof text!=='string'||text.length>160||!/^[a-zA-ZüÜvV'\s]+$/.test(text))return postMessage({id,result:null});
  const raw=text.trim(),lower=raw.toLowerCase();
  const short=raw===lower?shorthand[lower.replace(/[\s']/g,'')]:null;
  if(typeof short==='string')return postMessage({id,result:{text:short,candidates:[short],variants:[false],automatic:true,abbreviated:true,shorthand:true,ai:false,fuzzy:false,preedit:lower}});
  const single=raw===lower&&(force||!keepShortEnglish.has(lower))?singles[lower]:null;
  if(Array.isArray(single))return postMessage({id,result:{text:single[0],candidates:single,variants:single.map(()=>false),automatic:true,singleSyllable:true,ai:false,fuzzy:false,preedit:lower}});
  if(raw!==lower||words.has(lower)||(!force&&lower.split(/\s+/).some(w=>words.has(w))))return postMessage({id,result:null});
  const input=lower.replace(/ü/g,'v').replace(/\s+/g,"'"),r=query(input);
  const syllables=((r.head||'')+(r.body||'')+(r.tail||'')).split(/[ '\u200b]+/).filter(Boolean);
  if((r.tail||'')||!r.candidates?.length)return postMessage({id,result:null});
  const candidates=[],variants=[],fuzzyWords=new Map();
  const add=(t,isFuzzy)=>{if(t&&!candidates.includes(t)){candidates.push(t);variants.push(isFuzzy)}};
  for(let i=0;i<Math.min(r.candidates.length,4);i++)add(completeCandidate(input,i),false);
  if(fuzzy&&syllables.every(s=>s.length>=2))for(const v of fuzzyVariants(syllables)){
   const fr=query(v);if(!fr.tail&&fr.candidates?.length){
    const t=completeCandidate(v,0);add(t,true);
    const entry=(corpus[v.replace(/'/g,' ')]||[]).find(e=>e[0]===t);if(entry)fuzzyWords.set(t,entry[1]);
   }
  }
  keys('{Escape}');if(!candidates.length)return postMessage({id,result:null});
  // Rime supplies syllable segmentation. The mature corpus supplies complete
  // phrase alternatives across the whole sentence, not just the first segment.
  const direct=(corpus[syllables.join(' ')]||[]).slice(0,16).map(v=>v[0]);
  const ranked=direct.length?direct:corpusCandidates(syllables,candidates[0]);
  const all=[...ranked,...candidates],flags=[...ranked.map(()=>false),...variants],merged=[],mergedFlags=[];
  for(let i=0;i<all.length;i++)if(!merged.includes(all[i])){merged.push(all[i]);mergedFlags.push(flags[i])}
  // A short misspelling may have no dictionary word at all, only arbitrary
  // single-character combinations. Prefer well-attested fuzzy words, with
  // confirmation during automatic typing, rather than presenting those as words.
  const attested=merged.filter(t=>(fuzzyWords.get(t)||0)>=50000);
  if(!direct.length&&syllables.length<=4&&attested.length){merged.splice(0,merged.length,...attested);mergedFlags.splice(0,mergedFlags.length,...attested.map(()=>true));}
  const initials=syllables.filter(s=>s.length===1&&!/^[aoe]$/.test(s)).length;
  const boundedAbbreviation=syllables.length>=4&&initials<=2&&initials/syllables.length<=1/3&&lower.replace(/[^a-z]/g,'').length>=10;
  const automatic=merged[0].length>=2&&syllables.length>=2&&(initials===0||boundedAbbreviation)&&merged[0].length===syllables.length;
  postMessage({id,result:(force||automatic)?{text:merged[0],candidates:merged.slice(0,24),variants:mergedFlags.slice(0,24),automatic,abbreviated:initials>0,preedit:syllables.join(' '),ai:false,fuzzy:!!mergedFlags[0],suggestion:!force&&!!mergedFlags[0]}:null});
 }catch(e){postMessage({id,error:'离线词库不可用：'+e.message})}
};
function corpusCandidates(syllables,fallback){
 if(fallback.length!==syllables.length)return [];
 if(syllables.some(s=>s.length===1&&!/^[aoe]$/.test(s)))return [];
 const beam=Array.from({length:syllables.length+1},()=>[]);beam[0]=[{text:'',score:0}];
 for(let start=0;start<syllables.length;start++){
  const current=beam[start].sort((a,b)=>b.score-a.score).slice(0,10);
  for(let count=1;count<=Math.min(8,syllables.length-start);count++){
   const entries=corpus[syllables.slice(start,start+count).join(' ')]||[];
   const options=entries.slice(0,count===1?3:4);
   if(count===1&&!options.some(e=>e[0]===fallback[start]))options.push([fallback[start],Math.max(1,(entries[0]?.[1]||10000)*0.2)]);
   for(const prefix of current)for(const [text,weight]of options){
    if(text.length!==count)continue;
    // A fixed word-boundary cost prevents frequent single characters from
    // overwhelming actual words (e.g. two independent characters vs a noun).
    const value={text:prefix.text+text,score:prefix.score+Math.log((weight+1)/1000000000000)};
    const target=beam[start+count],existing=target.find(v=>v.text===value.text);
    if(existing){if(existing.score<value.score)existing.score=value.score}else target.push(value);
   }
   beam[start+count].sort((a,b)=>b.score-a.score);beam[start+count].splice(10);
  }
 }
 return beam[syllables.length].sort((a,b)=>b.score-a.score).slice(0,10).map(v=>v.text);
}
