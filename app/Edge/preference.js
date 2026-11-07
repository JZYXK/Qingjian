(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.QJPreference=api})(globalThis,()=>{
  const normalize=value=>String(value||'').toLowerCase().replace(/[\s']/g,'');
  function choose(records,text,candidates,context){
    const key=normalize(text),totals=new Map(),exact=new Map();
    for(const r of records){if(!r||r.pinyin!==key||!candidates.includes(r.text)||!Number.isFinite(r.hits))continue;
      totals.set(r.text,(totals.get(r.text)||0)+r.hits);
      if(context&&r.context===context)exact.set(r.text,(exact.get(r.text)||0)+r.hits);
    }
    const sorted=map=>[...map].sort((a,b)=>b[1]-a[1]);let ranked=sorted(exact),scope='context';
    if(!ranked.length||ranked[0][1]<2){ranked=sorted(totals);scope='habit';if(!ranked.length||ranked[0][1]<6||ranked[0][1]-(ranked[1]?.[1]||0)<3)return null}
    const index=candidates.indexOf(ranked[0][0]);return {index,text:ranked[0][0],scope};
  }
  function delta(message){return message.negative?-3:message.explicit===false?1:2}
  function apply(result,entry){if(!entry)return result;const flags=result.variants||[];return {...result,text:entry.text,candidates:[entry.text,...result.candidates.filter(t=>t!==entry.text)],variants:[!!flags[entry.index],...flags.filter((_,i)=>i!==entry.index)],fuzzy:!!flags[entry.index],learned:true,learningScope:entry.scope};}
  return {normalize,choose,delta,apply};
});
