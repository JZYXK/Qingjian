(()=>{
  const api=globalThis.chrome;
  if(!api?.runtime?.id||typeof api.runtime.sendMessage!=='function')return;
  const previous=globalThis.__qingjian;
  if(previous?.version==='0.5.4.1'&&previous.alive)return;
  previous?.stop?.();
  const controller=new AbortController();let alive=true,host;
  function dispose(){if(!alive)return;alive=false;controller.abort();clearTimeout(timer);clearTimeout(previewTimer);clearTimeout(aiTimer);host?.remove();state.alive=false;try{api.runtime?.onMessage?.removeListener(onMessage)}catch{}}
  const state={version:'0.5.4.1',alive:true,stop:dispose};globalThis.__qingjian=state;
  function live(){if(!api.runtime?.id){dispose();return false}return alive}
  function send(message){return Promise.resolve().then(()=>{if(!live())throw Error('扩展已更新，请刷新网页');return api.runtime.sendMessage(message)}).catch(error=>{if(!api.runtime?.id||/context invalidated/i.test(error?.message||''))dispose();throw error})}

  let enabled=true,active=true,siteBlocked=false,policyReady=false,topSite='',composing=false,timer,revision=0,lastUndo=null,mutating=false,lastTarget;
  let preview=null,suppressed=null,previewGeneration=0,previewTimer,aiTimer,session=0;const pendingCommits=[];
  const protectedNodes='[contenteditable="false"],[aria-readonly="true"],pre,code,[data-qingjian="off"],[role="terminal"],[role="code"],.monaco-editor,.CodeMirror,.cm-editor,input,textarea,table,img,iframe,button';
  function focused(){let el=document.activeElement;while(el?.shadowRoot?.activeElement)el=el.shadowRoot.activeElement;return el}
  const eventTarget=e=>e.composedPath?.()[0]||e.target;
  const siteKey=(()=>{try{const url=new URL(location.href);return /^https?:$/.test(url.protocol)?url.protocol+'//'+url.hostname:''}catch{return ''}})();
  async function loadPolicy(){
    policyReady=false;const policy=await send({type:'site-policy'});if(!alive)return;
    if(typeof policy?.blocked!=='boolean')throw Error('扩展正在更新，请刷新网页');
    enabled=policy.auto!==false;topSite=policy.topSite||'';siteBlocked=policy.blocked;policyReady=true;
  }
  let policyTask;function refreshPolicy(){return policyTask=loadPolicy().catch(()=>{siteBlocked=true;policyReady=true;if(!api.runtime?.id)dispose()})}
  refreshPolicy();
  host=document.createElement('div');host.style.cssText='position:fixed;z-index:2147483647;display:none';
  const shadow=host.attachShadow({mode:'closed'});
  shadow.innerHTML='<style>:host{all:initial;color-scheme:light}div{font:13px "Segoe UI","Microsoft YaHei",sans-serif;color:#222;background:#fff;border:1px solid #d9d9d9;border-radius:8px;box-shadow:0 3px 15px #0002;padding:8px 12px;max-width:400px}button{font:inherit;border:0;background:none;color:#315d8d;cursor:pointer;margin:0 5px;padding:2px}button[data-active]{background:#e8f0fe;color:#1457b8;border-radius:4px}small{color:#666}</style><div></div>';
  const box=shadow.querySelector('div');
  shadow.querySelector('style').textContent+='button[data-active]{background:#e8f0fe;border-radius:4px;color:#1457b8;font-weight:600}:host([data-qingjian-ui="candidates"]) div{display:flex;align-items:center;flex-wrap:wrap;gap:2px;max-width:600px}:host([data-qingjian-ui="candidates"]) small{flex-basis:100%;padding-top:3px}';
  host.setAttribute('data-qingjian-ui','hidden');
  function hide(){host.style.display='none';host.setAttribute('data-qingjian-ui','hidden')}
  function notice(el,text,actions=[]){
    if(!host.isConnected)document.documentElement.append(host);
    host.setAttribute('role','status');host.setAttribute('aria-label',text);
    box.replaceChildren(document.createTextNode(text));
    for(const [label,fn] of actions){const b=document.createElement('button');b.textContent=label;b.onmousedown=e=>e.preventDefault();b.onclick=fn;box.append(b)}
    const r=el.getBoundingClientRect();host.style.left=Math.max(8,Math.min(r.left,innerWidth-360))+'px';host.style.top=Math.max(8,Math.min(r.bottom+5,innerHeight-60))+'px';host.style.display='block';
  }
  function allowed(el){
    if(!active||siteBlocked||!policyReady||!el||!el.isConnected||el.closest('[data-qingjian="off"],[aria-readonly="true"],pre,code,[role="terminal"],[role="code"],.monaco-editor,.CodeMirror,.cm-editor'))return false;
    if(el.closest('[autocomplete="current-password"],[autocomplete="new-password"],[autocomplete="one-time-code"],[autocomplete^="cc-"]'))return false;
    if(el.matches('textarea'))return !el.readOnly&&!el.disabled;
    if(el.matches('input'))return (el.type==='text'||el.type==='search')&&!el.readOnly&&!el.disabled;
    return el.isContentEditable;
  }
  function target(node){
    let el=node?.nodeType===Node.TEXT_NODE?node.parentElement:node;
    if(!el?.closest)return null;
    const field=el.closest('input,textarea');if(field)return field;
    if(el.closest(protectedNodes))return null;
    if(!el.isContentEditable)return null;
    while(el.parentElement?.isContentEditable)el=el.parentElement;
    return el;
  }
  // Map text and line breaks to DOM positions without flattening editor markup.
  function editorModel(el){
    let value='';const units=[],block=/^(P|DIV|LI|BLOCKQUOTE|H[1-6])$/;
    const add=(text,a,b)=>{value+=text;units.push({a,b})};
    function walk(node){
      if(node.nodeType===Node.TEXT_NODE){for(let i=0;i<node.length;i++)add(node.data[i],[node,i],[node,i+1]);return}
      if(node.nodeType!==Node.ELEMENT_NODE)return;
      const parent=node.parentNode,index=parent?Array.prototype.indexOf.call(parent.childNodes,node):0;
      // Treat chips, attachments and code as atomic boundaries. A sibling chip
      // must not disable an otherwise editable prompt or join two pinyin tokens.
      if(node!==el&&node.matches(protectedNodes)){add('\ufffc',[parent,index],[parent,index+1]);return}
      if(node.tagName==='BR'){add('\n',[parent,index],[parent,index+1]);return}
      const isBlock=node!==el&&block.test(node.tagName);
      if(isBlock&&value&&!value.endsWith('\n'))add('\n',[parent,index],[node,0]);
      for(const child of node.childNodes)walk(child);
      if(isBlock&&node.nextSibling&&!value.endsWith('\n'))add('\n',[node,node.childNodes.length],[parent,index+1]);
    }
    walk(el);
    function pointRange(point){const r=document.createRange();r.setStart(...point);r.collapse(true);return r}
    function offset(node,index){
      const p=pointRange([node,index]);
      for(let i=0;i<units.length;i++){
        if(p.compareBoundaryPoints(Range.START_TO_START,pointRange(units[i].a))<=0)return i;
        if(p.compareBoundaryPoints(Range.START_TO_START,pointRange(units[i].b))<=0)return i+1;
      }
      return units.length;
    }
    const point=index=>units[index]?.a||units[units.length-1]?.b||[el,0];
    return {value,offset,point};
  }
  function snapshot(el){
    if(!allowed(el))return null;
    if('value'in el)return {el,value:el.value,start:el.selectionStart,end:el.selectionEnd};
    const sel=getSelection();if(!sel?.rangeCount||target(sel.anchorNode)!==el||target(sel.focusNode)!==el)return null;
    const range=sel.getRangeAt(0).cloneRange(),model=editorModel(el);
    for(const island of el.querySelectorAll(protectedNodes))if(range.intersectsNode(island))return null;
    return {el,value:model.value,start:model.offset(range.startContainer,range.startOffset),end:model.offset(range.endContainer,range.endOffset),range,markup:el.innerHTML};
  }
  function select(el,start,end,range){
    if('value'in el){el.setSelectionRange(start,end);return}
    const r=range?.cloneRange()||document.createRange();
    if(!range){const model=editorModel(el);if(start<0||end>model.value.length)return false;r.setStart(...model.point(start));r.setEnd(...model.point(end))}
    getSelection().removeAllRanges();getSelection().addRange(r);
  }
  function replace(s,start,end,text,caret,undoable=true){
    const now=snapshot(s.el);
    if(composing||!now||now.value!==s.value||now.start!==s.start||now.end!==s.end||focused()!==s.el)return false;
    const next=s.value.slice(0,start)+text+s.value.slice(end),old=s.value.slice(start,end);
    mutating=true;
    try{
      if(select(s.el,start,end,start===s.start&&end===s.end?now.range:null)===false)return false;
      // Native editor transaction retains the browser's undo history and input events.
      if(!document.execCommand('insertText',false,text))return false;
      const actual='value'in s.el?s.el.value:editorModel(s.el).value;
      if(actual!==next)return false;
      const pos=caret??start+text.length;select(s.el,pos,pos);
      if(undoable)lastUndo={el:s.el,value:next,start,end:start+text.length,old};
      revision++;return true;
    }finally{mutating=false}
  }
  function remember(payload,negative=false,explicit=true){if(payload)send({type:'learn',...payload,negative,explicit}).catch(()=>{})}
  function undo(){const u=lastUndo;if(!u)return;u.el.focus();const s=snapshot(u.el);if(s&&s.value===u.value&&replace(s,u.start,u.end,u.old,undefined,false)){remember(u.learning,true);lastUndo=null;hide()}}
  async function query(text,force,before='',after='',background=false){try{return await send({type:'query',text,force,before:before.slice(-48),after:after.slice(0,48),preview:background})}catch{return {error:'扩展已关闭，请刷新网页'}}}
  function cancelCandidates(){preview=null;suppressed=null;previewGeneration++;session++;clearTimeout(previewTimer);clearTimeout(aiTimer);pendingCommits.length=0;}
  function retainEnglish(){const el=preview?.s.el||target(focused()),s=snapshot(el);cancelCandidates();if(s)suppressed={el,value:s.value};hide()}
  async function fastQuery(text,before='',after=''){try{return await send({type:'query-fast',text,force:false,before:before.slice(-48),after:after.slice(0,48)})}catch{return {result:null}}}
  function token(s){
    if(!s||s.start!==s.end)return null;
    const m=s.value.slice(0,s.start).match(/[A-Za-zü']{2,100}$/);if(!m)return null;
    const start=s.start-m[0].length;if(start&&/[A-Za-z0-9_@/:.\-]/.test(s.value[start-1]))return null;
    return {s,start,end:s.start,text:m[0]};
  }
  function validPreview(p){const s=snapshot(p.s.el);return !composing&&s&&focused()===p.s.el&&s.value===p.s.value&&s.start===p.s.start&&s.end===p.s.end;}
  function updatePending(el,start,end,text){
    const delta=text.length-(end-start);
    for(const p of pendingCommits)if(p.s.el===el&&p.start>=end){p.s.value=p.s.value.slice(0,start)+text+p.s.value.slice(end);p.start+=delta;p.end+=delta;}
  }
  function commitPreview(p,index=0,confirmed=false){
    if(!validPreview(p))return false;const text=p.result.candidates[index];if(!text)return false;
    if(!replace(p.s,p.start,p.end,text))return false;
    lastUndo.learning={text:p.text,choice:text,before:p.s.value.slice(Math.max(0,p.start-48),p.start),after:p.s.value.slice(p.end,p.end+48)};
    remember(lastUndo.learning,false,confirmed);
    updatePending(p.s.el,p.start,p.end,text);preview=null;previewGeneration++;clearTimeout(aiTimer);hide();return true;
  }
  function renderPreview(p){
    const choices=p.result.candidates.slice(0,5),actions=choices.map((text,index)=>[(index+1)+' '+text,()=>commitPreview(p,index,true)]);
    actions.push(['保留英文',retainEnglish]);
    notice(p.s.el,'',actions);host.setAttribute('data-qingjian-ui','candidates');
    host.setAttribute('aria-label','拼音候选：'+choices.join('、'));
    const buttons=box.querySelectorAll('button');if(buttons[p.index])buttons[p.index].setAttribute('data-active','');
    const hint=document.createElement('small');hint.textContent=' 空格上屏 · 数字选词 · Esc 保留'+(p.result.ai?' · AI':'');box.append(hint);
  }
  function requestPreview(el){
    const generation=++previewGeneration;clearTimeout(previewTimer);clearTimeout(aiTimer);preview=null;
    const t=token(snapshot(el));if(!t||!enabled||!active||composing){hide();return}
    previewTimer=setTimeout(async()=>{
      const r=await fastQuery(t.text,t.s.value.slice(0,t.start),t.s.value.slice(t.end));if(generation!==previewGeneration||!r.result||!validPreview({...t,result:r.result})){if(generation===previewGeneration)hide();return}
      preview={...t,result:r.result,index:0};renderPreview(preview);
      aiTimer=setTimeout(async()=>{
        const current=preview;if(!current||generation!==previewGeneration)return;
        const r=await query(t.text,false,t.s.value.slice(0,t.start),t.s.value.slice(t.end),true);
        if(preview!==current||generation!==previewGeneration||!validPreview(current)||!r.result?.ai&&!r.result?.learned)return;
        const candidates=[r.result.text,...r.result.candidates.filter(c=>c!==r.result.text)];current.result={...r.result,candidates};current.index=0;renderPreview(current);
      },650);
    },25);
  }
  function pendingBoundary(el){
    const s=snapshot(el);if(!s||s.start!==s.end)return;
    const m=s.value.slice(0,s.start).match(/([A-Za-zü']{2,100})([ ,.?!，。！？])$/);if(!m)return;
    if(suppressed?.el===el&&suppressed.value===s.value.slice(0,s.start-1)){suppressed=null;return}
    const start=s.start-m[0].length;if(start&&/[A-Za-z0-9_@/:.\-]/.test(s.value[start-1]))return;
    const p={s,start,end:s.start,text:m[1],delimiter:m[2],session};pendingCommits.push(p);
    fastQuery(p.text,p.s.value.slice(0,p.start),p.s.value.slice(p.end)).then(r=>{
      if(!r.result||p.session!==session||!enabled||composing)return;
      const now=snapshot(el);if(!now||focused()!==el||now.start!==now.end||now.start!==now.value.length||!now.value.startsWith(p.s.value))return;
      const text=r.result.text+(p.delimiter===' '?'':p.delimiter),caret=now.start+text.length-(p.end-p.start);
      if(replace(now,p.start,p.end,text,caret)){
        lastUndo.learning={text:p.text,choice:r.result.text,before:now.value.slice(Math.max(0,p.start-48),p.start),after:now.value.slice(p.end,p.end+48)};
        remember(lastUndo.learning,false,false);
        updatePending(el,p.start,p.end,text);preview=null;previewGeneration++;hide();
      }
    }).finally(()=>{const i=pendingCommits.indexOf(p);if(i>=0)pendingCommits.splice(i,1)});
  }
  async function convert(){
    await policyTask;if(!active||siteBlocked||!policyReady)return;
    cancelCandidates();
    const el=target(focused())||lastTarget,s=focused()===el?snapshot(el):null;
    if(focused()?.matches?.('input[type="password"],[autocomplete="one-time-code"],[autocomplete^="cc-"]'))return;
    if(composing)return;
    if(!s||s.start===s.end){
      const selection=getSelection();
      if(target(selection?.anchorNode)||target(selection?.focusNode)){notice(el||document.activeElement,'此编辑区域暂不支持安全替换');return}
      const text=selection?.toString();if(!text||text.length>160||!/^[a-züv'\s]+$/.test(text))return;
      const range=selection.getRangeAt(0).cloneRange(),generation=++revision,anchor={getBoundingClientRect:()=>range.getBoundingClientRect()};
      notice(anchor,'正在本地转换…');const r=await query(text,true);
      if(generation!==revision||getSelection()?.toString()!==text||!r.result){hide();return}
      notice(anchor,'只读选区：'+r.result.text,[['复制结果',()=>navigator.clipboard?.writeText(r.result.text).then(()=>hide()).catch(()=>notice(anchor,'请选中并复制：'+r.result.text))],['关闭',hide]]);return;
    }
    const text=s.value.slice(s.start,s.end),r0=++revision;notice(el,'正在本地转换…');
    const {result,error}=await query(text,true,s.value.slice(0,s.start),s.value.slice(s.end));if(r0!==revision||composing){hide();return}
    if(!result){notice(el,error||'未识别出完整拼音，原文已保留');return}
    const payload={text,before:s.value.slice(Math.max(0,s.start-48),s.start),after:s.value.slice(s.end,s.end+48)};
    if(replace(s,s.start,s.end,result.text)){
      lastUndo.learning={...payload,choice:result.text};
      const actions=[['撤销',undo]];
      for(const candidate of result.candidates.filter(c=>c!==result.text).slice(0,3))actions.push([candidate,()=>{
        const u=lastUndo;if(!u)return;const fresh=snapshot(el);if(fresh&&fresh.value===u.value&&replace(fresh,u.start,u.end,candidate)){
          lastUndo.old=u.old;notice(el,'已改为：'+candidate,[['撤销',undo]]);
          lastUndo.learning={...payload,choice:candidate};remember(lastUndo.learning);
        }
      }]);
      notice(el,'已转为 '+result.text+(result.learned?' · 已学习偏好':result.ai?' · 本地 AI': ' · '+(result.aiStatus||'离线词库')),actions);
    }else hide();
  }
  document.addEventListener('focusin',e=>{lastTarget=target(eventTarget(e));revision++;cancelCandidates();hide()},{capture:true,signal:controller.signal});
  document.addEventListener('compositionstart',()=>{composing=true;revision++;cancelCandidates();clearTimeout(timer);hide()},{capture:true,signal:controller.signal});
  document.addEventListener('compositionend',()=>{composing=false;revision++;clearTimeout(timer)},{capture:true,signal:controller.signal});
  window.addEventListener('keydown',e=>{
    if(!live()||!active||siteBlocked||!policyReady)return;
    if(!e.isTrusted&&location.protocol!=='chrome-extension:')return;
    if(e.isComposing||composing||e.keyCode===229)return;
    if(e.ctrlKey&&e.shiftKey&&!e.altKey&&e.code==='KeyZ'){
      const s=snapshot(target(eventTarget(e)));if(s&&s.start!==s.end||getSelection()?.toString()){e.preventDefault();e.stopImmediatePropagation();convert()}
    }else if(e.ctrlKey||e.altKey||e.metaKey){revision++;cancelCandidates();hide()}
    else if(e.key==='Escape'){revision++;retainEnglish()}
    else if(preview&&validPreview(preview)){
      if(e.key===' '){if(commitPreview(preview,preview.index)){e.preventDefault();e.stopImmediatePropagation();}}
      else if(/^[1-5]$/.test(e.key)){if(commitPreview(preview,Number(e.key)-1,true)){e.preventDefault();e.stopImmediatePropagation();}}
      else if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();const n=Math.min(5,preview.result.candidates.length);preview.index=(preview.index+(e.key==='ArrowDown'?1:n-1))%n;renderPreview(preview);}
      else if(['ArrowLeft','ArrowRight','Home','End','Enter','Backspace','Delete'].includes(e.key)){cancelCandidates();hide()}
    }else if(['ArrowLeft','ArrowRight','Home','End','Enter','Backspace','Delete'].includes(e.key)){cancelCandidates();hide()}
  },{capture:true,signal:controller.signal});
  document.addEventListener('input',e=>{
    if(!live())return;
    if(!e.isTrusted&&location.protocol!=='chrome-extension:')return;
    if(mutating)return;const r0=++revision;clearTimeout(timer);hide();
    if(!enabled||composing||e.isComposing)return;
    const el=target(eventTarget(e));if(!allowed(el))return;
    if(e.inputType==='historyUndo'&&lastUndo?.el===el){const u=lastUndo,s=snapshot(el);if(s&&s.value===u.value.slice(0,u.start)+u.old+u.value.slice(u.end)){remember(u.learning,true);lastUndo=null;}return}
    // Let React/Vue finish their input handler and possible DOM remount before
    // taking the snapshot. Resolve the current focused editor again afterwards.
    const boundary=e.inputType==='insertText'&&/[ ,.?!，。！？]/.test(e.data||'');
    // Capture word boundaries now: a following key must not cancel this commit.
    if(boundary){preview=null;previewGeneration++;clearTimeout(previewTimer);clearTimeout(aiTimer);pendingBoundary(el);return}
    if(['insertText','deleteContentBackward','insertFromPaste'].includes(e.inputType))timer=setTimeout(()=>{if(r0!==revision||composing||!enabled||!active)return;const current=target(focused());if(allowed(current))requestPreview(current)},0);
  },{capture:true,signal:controller.signal});
  document.addEventListener('pointerdown',e=>{if(e.target!==host){revision++;cancelCandidates();hide()}},{capture:true,signal:controller.signal});
  function onMessage(m,sender,reply){
    if(!live())return;
    if(m.type==='convert')convert();
    if(m.type==='deactivate'){active=false;revision++;cancelCandidates();clearTimeout(timer);hide()}
    if(m.type==='activate'||m.type==='refresh-policy'){if(m.type==='refresh-policy'||!active){revision++;cancelCandidates();hide()}if(m.type==='activate')active=true;refreshPolicy().then(()=>reply?.({ready:policyReady,blocked:siteBlocked}));return true;}
  }
  try{api.runtime.onMessage.addListener(onMessage)}catch{dispose();return}
  send({type:'warm-fast'}).catch(()=>{});
})();
