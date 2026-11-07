importScripts('preference.js');
let creating;
let previewAIInFlight=false;
let confirmedCache=[],learnQueue=Promise.resolve();
let disabledSites=[],autoEnabled=true;
const siteSettingsReady=chrome.storage.local.get({disabledSites:[],auto:true}).then(s=>{disabledSites=Array.isArray(s.disabledSites)?s.disabledSites:[];autoEnabled=s.auto!==false});
function siteKey(url){try{const u=new URL(url);return /^https?:$/.test(u.protocol)?u.protocol+'//'+u.hostname:''}catch{return ''}}
function urlBlocked(url){return disabledSites.includes(siteKey(url))}
async function senderPolicy(sender){await siteSettingsReady;const topSite=siteKey(sender.tab?.url),frameSite=siteKey(sender.url)||siteKey(sender.origin);return {topSite,auto:autoEnabled,blocked:disabledSites.includes(topSite)||disabledSites.includes(frameSite)||!!sender.tab&&sender.frameId>0&&!topSite};}
async function setSiteDisabled(url,disabled){await siteSettingsReady;const key=siteKey(url);if(!key)throw Error('仅支持普通网页');disabledSites=disabled?[...new Set([...disabledSites,key])]:disabledSites.filter(s=>s!==key);if(disabledSites.length>500)throw Error('排除站点过多');await chrome.storage.local.set({disabledSites});await syncGrantedSites(true);return {ok:true,disabled,site:key};}
chrome.storage.local.get({confirmedCache:[]}).then(s=>confirmedCache=s.confirmedCache);
function broadcastPolicy(){chrome.tabs.query({}).then(tabs=>Promise.all(tabs.map(tab=>chrome.tabs.sendMessage(tab.id,{type:'refresh-policy'}).catch(()=>{})))).catch(()=>{})}
chrome.storage.onChanged.addListener(c=>{if(c.auto){autoEnabled=c.auto.newValue!==false;broadcastPolicy()}if(c.confirmedCache)confirmedCache=c.confirmedCache.newValue||[];if(c.disabledSites){disabledSites=Array.isArray(c.disabledSites.newValue)?c.disabledSites.newValue:[];restoreGrantedSites(true)}});
const normalize=s=>String(s||'').toLowerCase().replace(/[\s']/g,'');
async function contextHash(m){const text=String(m.before||'').slice(-8)+'|'+String(m.after||'').slice(0,4);if(text==='|')return '';return [...new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(text)))].map(b=>b.toString(16).padStart(2,'0')).join('')}
async function cacheConfirmation(m){
  const text=String(m.choice||''),pinyin=normalize(m.text);if(!/^[\u3400-\u9fff]{1,16}$/.test(text)||!/^[a-züv]{2,60}$/.test(pinyin))return;
  const context=await contextHash(m);let entry=confirmedCache.find(e=>e.text===text&&e.pinyin===pinyin&&e.context===context);
  if(!entry){entry={text,pinyin,context,hits:0,last:0};confirmedCache.push(entry)}entry.hits=Math.max(-10,Math.min(40,entry.hits+QJPreference.delta(m)));entry.last=Date.now();confirmedCache.sort((a,b)=>b.last-a.last);confirmedCache=confirmedCache.slice(0,1000);await chrome.storage.local.set({confirmedCache});
}
async function applyConfirmation(m,r){
  if(!r?.result)return r;const context=await contextHash(m);
  r.result=QJPreference.apply(r.result,QJPreference.choose(confirmedCache,m.text,r.result.candidates,context));
  return r;
}
let port,serial=0;const waiting=new Map();
function nativeQuery(message){
 return new Promise((resolve,reject)=>{
  if(!port){
   port=chrome.runtime.connectNative('com.qingjian.local');
   port.onMessage.addListener(r=>{const p=waiting.get(r.requestId);if(p){waiting.delete(r.requestId);clearTimeout(p.timer);p.resolve(r)}});
   port.onDisconnect.addListener(()=>{const reason=chrome.runtime.lastError?.message||'本地助手未连接';port=null;for(const p of waiting.values()){clearTimeout(p.timer);p.reject(Error(reason))}waiting.clear()});
  }
  const requestId=++serial,timer=setTimeout(()=>{waiting.delete(requestId);reject(Error('本地 AI 超时'))},message.force?65000:20000);
  waiting.set(requestId,{resolve,reject,timer});
  try{port.postMessage({...message,requestId})}catch(e){waiting.delete(requestId);clearTimeout(timer);reject(e)}
 });
}
async function engine(){
  if((await chrome.runtime.getContexts({contextTypes:['OFFSCREEN_DOCUMENT']})).length)return;
  if(!creating)creating=chrome.offscreen.createDocument({url:'offscreen.html',reasons:['WORKERS'],justification:'在本地 Worker 中运行离线 Rime 词库，不读取其他网页'}).finally(()=>creating=null);
  await creating;
}
// Restore only origins already granted by the user. Serialize concurrent repairs.
let siteSync=Promise.resolve();
async function siteScriptId(pattern){const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(pattern));return 'site-v2-'+[...new Uint8Array(digest)].map(b=>b.toString(16).padStart(2,'0')).join('')}
async function synchronizeSites(reset=false){
  await siteSettingsReady;
  const {origins=[]}=await chrome.permissions.getAll();
  const patterns=[...new Set(origins.filter(p=>/^(https?|\*):\/\//.test(p)))];
  const desired=await Promise.all(patterns.map(async pattern=>({id:await siteScriptId(pattern),matches:[pattern],excludeMatches:disabledSites.map(s=>s+'/*'),js:['content.js'],world:'ISOLATED',runAt:'document_start',allFrames:true,matchOriginAsFallback:true,persistAcrossSessions:true})));
  const registered=await chrome.scripting.getRegisteredContentScripts(),desiredIds=new Set(desired.map(script=>script.id));
  const obsolete=registered.filter(script=>script.id.startsWith('site-')&&!desiredIds.has(script.id)).map(script=>script.id);
  if(obsolete.length)await chrome.scripting.unregisterContentScripts({ids:obsolete});
  const ids=new Set(registered.map(script=>script.id)),missing=desired.filter(script=>!ids.has(script.id)),existing=desired.filter(script=>ids.has(script.id));
  if(missing.length)await chrome.scripting.registerContentScripts(missing);
  if(existing.length)await chrome.scripting.updateContentScripts(existing);
  const tabs=patterns.length?(await chrome.tabs.query({url:patterns})).filter(t=>!urlBlocked(t.url)):[];
  if(reset){const allowed=new Set(tabs.map(t=>t.id));const all=await chrome.tabs.query({});await Promise.all(all.filter(t=>!allowed.has(t.id)&&!(t.url||'').startsWith(chrome.runtime.getURL(''))).map(t=>chrome.tabs.sendMessage(t.id,{type:'deactivate'}).catch(()=>{})))}
  const activated=[],failed=[];
  await Promise.all(tabs.map(async tab=>{try{await chrome.scripting.executeScript({target:{tabId:tab.id,allFrames:true},files:['content.js'],world:'ISOLATED'});await chrome.tabs.sendMessage(tab.id,{type:'activate'});activated.push(tab.id)}catch{failed.push(tab.id)}}));
  return {ok:true,activated,failed};
}
function syncGrantedSites(reset=false){const run=siteSync.catch(()=>{}).then(()=>synchronizeSites(reset));siteSync=run;return run}
function restoreGrantedSites(reset=false){syncGrantedSites(reset).catch(()=>{})}
chrome.permissions.onAdded.addListener(()=>restoreGrantedSites());
chrome.permissions.onRemoved.addListener(()=>restoreGrantedSites(true));
chrome.runtime.onStartup.addListener(()=>restoreGrantedSites());
chrome.runtime.onInstalled.addListener(()=>restoreGrantedSites());
restoreGrantedSites();
function handleMessage(m,s,reply){
  if(m.type==='site-policy'){senderPolicy(s).then(reply).catch(()=>reply({blocked:true}));return true;}
  if(m.type==='set-site-disabled'){if(!(s.url||'').startsWith(chrome.runtime.getURL(''))){reply({ok:false});return}setSiteDisabled(m.url,!!m.disabled).then(reply).catch(e=>reply({ok:false,error:e.message}));return true;}
  if(m.type==='sync-granted-sites'){syncGrantedSites(!!m.reset).then(reply).catch(e=>reply({ok:false,error:e.message}));return true;}
  if(m.target==='engine')return;
  if(m.type==='native-status'){
    nativeQuery({action:'status'}).then(reply).catch(()=>reply({unavailable:true}));return true;
  }
  if(m.type==='warm-fast'){
    engine().then(()=>chrome.runtime.sendMessage({target:'engine',text:'',force:false})).then(reply).catch(()=>reply({result:null}));return true;
  }
  if(m.type==='query-fast'){
    if(typeof m.text!=='string'||m.text.length>160){reply({result:null});return}
    chrome.storage.local.get({fuzzy:true,learning:true}).then(async s=>{await engine();const r=await chrome.runtime.sendMessage({target:'engine',text:m.text,force:!!m.force,fuzzy:s.fuzzy});return s.learning?applyConfirmation(m,r):r}).then(reply).catch(()=>reply({result:null}));return true;
  }
  if(m.type==='learn'){
    chrome.storage.local.get({learning:true}).then(async s=>{if(!s.learning)return {ok:false,disabled:true};learnQueue=learnQueue.catch(()=>{}).then(()=>cacheConfirmation(m));await learnQueue;try{await nativeQuery({action:'learn',text:m.text,choice:m.choice,before:String(m.before||'').slice(-48),after:String(m.after||'').slice(0,48),negative:!!m.negative,explicit:m.explicit!==false})}catch{}return {ok:true}}).then(reply).catch(()=>reply({ok:false}));return true;
  }
  if(m.type==='clear-learning'){
    confirmedCache=[];chrome.storage.local.set({confirmedCache:[]}).then(()=>nativeQuery({action:'clearLearning'})).then(reply).catch(()=>reply({ok:false,browserCleared:true}));return true;
  }
  if(m.type==='query'){
    if(typeof m.text!=='string'||m.text.length>160){reply({result:null});return}
    (async()=>{
      const settings=await chrome.storage.local.get({ai:true,fuzzy:true,learning:true});
      if(settings.ai&&(!m.preview||!previewAIInFlight)){
        if(m.preview)previewAIInFlight=true;
        try{const r=await nativeQuery({text:m.text,force:!!m.force,ai:true,latencyFirst:!!m.force,fuzzy:settings.fuzzy,learning:settings.learning,budgetMs:m.preview?6000:650,preview:!!m.preview,before:String(m.before||'').slice(-48),after:String(m.after||'').slice(0,48)});if(!r.unavailable&&!r.error)return r}catch{}finally{if(m.preview)previewAIInFlight=false;}
      }
      await engine();const r=await chrome.runtime.sendMessage({target:'engine',text:m.text,force:!!m.force,fuzzy:settings.fuzzy});
      if(r?.result)r.result.aiStatus=settings.ai?'词库回退：请启动清简助手并完成桥接安装':'AI已关闭';return r;
    })().then(async r=>(await chrome.storage.local.get({learning:true})).learning?applyConfirmation(m,r):r).then(reply).catch(e=>reply({error:e.message}));return true;
  }
}
chrome.runtime.onMessage.addListener((m,s,reply)=>{
 if(s.tab&&['warm-fast','query-fast','query','learn'].includes(m.type)){senderPolicy(s).then(policy=>{if(policy.blocked)reply({result:null,disabled:true});else handleMessage(m,s,reply)}).catch(()=>reply({result:null}));return true;}
 return handleMessage(m,s,reply);
});
async function convertInTab(tab,frameId=0){
  if(!tab)return;
  await siteSettingsReady;if(urlBlocked(tab.url))return;
  try{
    if(/^https?:\/\//.test(tab.url||''))await chrome.scripting.executeScript({target:{tabId:tab.id,frameIds:[frameId]},files:['content.js'],world:'ISOLATED'});
    await chrome.tabs.sendMessage(tab.id,{type:'activate'},{frameId});
    await chrome.tabs.sendMessage(tab.id,{type:'convert'},{frameId});
  }catch{/* Restricted browser pages and inaccessible frames are left unchanged. */}
}
chrome.commands.onCommand.addListener(async command=>{
  if(command==='convert'){
    const [tab]=await chrome.tabs.query({active:true,currentWindow:true});
    await convertInTab(tab);
  }
});
chrome.runtime.onInstalled.addListener(()=>{
  chrome.contextMenus.removeAll(()=>{chrome.contextMenus.create({id:'convert',title:'清简：选中拼音转汉字',contexts:['editable','selection']});chrome.contextMenus.create({id:'toggle-site',title:'在此网站禁用／恢复清简',contexts:['action'],documentUrlPatterns:['http://*/*','https://*/*']});});
});
chrome.contextMenus.onClicked.addListener((info,tab)=>{
  if(info.menuItemId==='convert')convertInTab(tab,info.frameId||0);
  if(info.menuItemId==='toggle-site'&&tab?.url)siteSettingsReady.then(()=>setSiteDisabled(tab.url,!urlBlocked(tab.url))).catch(()=>{});
});
