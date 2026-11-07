const worker=new Worker('engine-v2.js');let serial=0;const pending=new Map();
worker.onmessage=({data})=>{const p=pending.get(data.id);if(p){clearTimeout(p.timer);pending.delete(data.id);p.resolve(data)}};
worker.onerror=()=>{for(const p of pending.values()){clearTimeout(p.timer);p.resolve({error:'离线引擎启动失败'})}pending.clear()};
chrome.runtime.onMessage.addListener((m,s,reply)=>{
  if(m.target!=='engine')return;
  const id=++serial;
  const timer=setTimeout(()=>{pending.delete(id);reply({error:'转换超时，原文已保留'})},15000);
  pending.set(id,{resolve:reply,timer});worker.postMessage({id,text:m.text,force:m.force,fuzzy:m.fuzzy});return true;
});
