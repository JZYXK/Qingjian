'use strict';
const fs=require('fs'),crypto=require('crypto'),path=require('path');
const preference=require('./Edge/preference.js');
const file=process.env.QJ_LEARNING_FILE||path.join(__dirname,'user-data','confirmed-choices.json');let records=[];
try{const saved=JSON.parse(fs.readFileSync(file));if(Array.isArray(saved))records=saved.slice(0,1000)}catch{}
const normalize=s=>String(s||'').toLowerCase().replace(/[\s']/g,'');
const context=m=>{const s=String(m.before||'').slice(-8)+'|'+String(m.after||'').slice(0,4);return s==='|'?'':crypto.createHash('sha256').update(s).digest('hex')};
function save(){fs.mkdirSync(path.dirname(file),{recursive:true});const temp=file+'.tmp';fs.writeFileSync(temp,JSON.stringify(records));fs.renameSync(temp,file)}
exports.record=m=>{
 const pinyin=normalize(m.text),text=String(m.choice||'');
 if(!/^[a-zvü]{2,60}$/.test(pinyin)||!/^[\u3400-\u9fff]{1,16}$/.test(text))return {ok:false};
 const hash=context(m);let r=records.find(r=>r.pinyin===pinyin&&r.text===text&&r.context===hash);
 if(!r){r={pinyin,text,context:hash,hits:0,last:0};records.push(r)}
 r.hits=Math.max(-10,Math.min(40,r.hits+preference.delta(m)));r.last=Date.now();
 records.sort((a,b)=>b.last-a.last);records=records.slice(0,1000);save();return {ok:true};
};
exports.hints=(m,candidates)=>{const key=normalize(m.text),hash=context(m);return records.filter(r=>r.pinyin===key&&candidates.includes(r.text)).map(r=>({choice:candidates.indexOf(r.text),hits:r.hits,sameContext:!!hash&&r.context===hash})).sort((a,b)=>(b.sameContext-a.sameContext)||b.hits-a.hits).slice(0,8)};
exports.preferred=(m,candidates)=>preference.choose(records,m.text,candidates,context(m));
exports.clear=()=>{records=[];save();return {ok:true}};
