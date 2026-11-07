let tab,pattern,granted=false,blocked=false;
const allOrigins=['https://*/*','http://*/*'],allSites=document.querySelector('#all-sites');
const site=document.querySelector('#site'),status=document.querySelector('#status'),auto=document.querySelector('#auto');
const exclude=document.querySelector('#exclude-site');
chrome.storage.local.get({auto:true}).then(s=>auto.checked=s.auto);
auto.onchange=()=>chrome.storage.local.set({auto:auto.checked});
async function syncSites(reset=false){const r=await chrome.runtime.sendMessage({type:'sync-granted-sites',reset});if(!r?.ok)throw Error(r?.error||'网站启用未完成');return r}
async function init(repair=true){
 [tab]=await chrome.tabs.query({active:true,currentWindow:true});
 if(!/^https?:\/\//.test(tab?.url||'')){site.disabled=true;exclude.disabled=true;site.textContent='请在普通网页中启用';return}
 const url=new URL(tab.url);pattern=url.protocol+'//'+url.hostname+'/*';granted=await chrome.permissions.contains({origins:[pattern]});const global=await chrome.permissions.contains({origins:allOrigins});const settings=await chrome.storage.local.get({disabledSites:[]});blocked=settings.disabledSites.includes(url.protocol+'//'+url.hostname);allSites.checked=global;site.disabled=global;site.textContent=global?(blocked?'全站已授权，此站已禁用':'全站已启用，无需逐站开启'):granted?'撤销此网站授权':'在此网站启用';exclude.disabled=false;exclude.textContent=blocked?'恢复此网站':'在此网站禁用';
 if(granted&&repair)await syncSites();
}
site.onclick=async()=>{
 site.disabled=true;
 try{
  if(granted){
   const tabs=await chrome.tabs.query({url:pattern});
   await Promise.all(tabs.map(t=>chrome.tabs.sendMessage(t.id,{type:'deactivate'}).catch(()=>{})));
   const removed=await chrome.permissions.remove({origins:[pattern]});await syncSites(true);
   status.textContent=removed?'已停用；无需关闭浏览器。':'权限未移除，请检查扩展的网站权限。';
  }else if(await chrome.permissions.request({origins:[pattern]})){
   if(blocked)await chrome.runtime.sendMessage({type:'set-site-disabled',url:tab.url,disabled:false});
   const r=await syncSites();status.textContent=r.failed.includes(tab.id)?'授权已保存，此页面限制扩展运行。':'已启用此网站所有已打开页面，输入即可，无需先按快捷键。';
  }
  await init(false);
 }catch(e){status.textContent='未能启用：'+e.message}finally{site.disabled=false}
};
exclude.onclick=async()=>{
 exclude.disabled=true;
 try{const r=await chrome.runtime.sendMessage({type:'set-site-disabled',url:tab.url,disabled:!blocked});if(!r?.ok)throw Error(r?.error||'设置未完成');status.textContent=r.disabled?'此站已禁用，其他网站不受影响。Edge网站访问授权仍保留。':'已恢复此站；如尚未授权，请点击“在此网站启用”。';await init(false);}catch(e){status.textContent='站点设置未完成：'+e.message}finally{exclude.disabled=false}
};
chrome.permissions.contains({origins:allOrigins}).then(value=>allSites.checked=value);
allSites.onchange=async()=>{
 allSites.disabled=true;
 try{
  if(allSites.checked){if(!await chrome.permissions.request({origins:allOrigins})){allSites.checked=false;return}const r=await syncSites();status.textContent=r.failed.includes(tab?.id)?'全站授权已保存，当前页面受限。请刷新，并检查 Edge 扩展的网站访问权限。':'全站已启用，新网站自动生效。密码和受保护区域仍不处理。'}
  else{const removed=await chrome.permissions.remove({origins:allOrigins});await syncSites(true);status.textContent=removed?'已关闭全站权限；可按需重新启用单个网站。':'未能撤销全站权限，请在 Edge 扩展详情中检查网站访问设置。'}
  await init(false);
 }catch(e){status.textContent='全站设置未完成：'+e.message;allSites.checked=await chrome.permissions.contains({origins:allOrigins})}finally{allSites.disabled=false}
};
document.querySelector('#keys').onclick=()=>chrome.tabs.create({url:'edge://extensions/shortcuts'});
init().catch(e=>status.textContent='未能恢复网站设置：'+e.message);
for(const name of ['ai','fuzzy','learning']){
 const box=document.querySelector('#'+name);chrome.storage.local.get({[name]:true}).then(s=>box.checked=s[name]);box.onchange=()=>chrome.storage.local.set({[name]:box.checked});
}
chrome.runtime.sendMessage({type:'native-status'}).then(r=>document.querySelector('#native').textContent=r.ok?(r.dictionaryError?'词库准备失败，请查看桌面助手状态':r.dictionaryReady===false?'桌面词库准备中，网页本地候选仍可用':r.model?'本地助手就绪 · '+(r.modelName||'AI')+' · '+(r.aiLoaded?'已加载':r.aiRunning?'后台加载中':'按需加载'):'词库就绪，AI模型缺失'):'网页词库可直接使用；本地 AI 请启动桌面清简').catch(()=>{});
document.querySelector('#clear-learning').onclick=()=>chrome.runtime.sendMessage({type:'clear-learning'}).then(r=>status.textContent=r.ok?'已清除本地确认记录':r.browserCleared?'网页记录已清除；桌面记录请启动助手后再清除':'请先启动本地助手');
