// Reuse already downloaded, pinned dictionary files; do not fetch again.
const fs=require('fs'),path=require('path'),base=path.resolve(__dirname,'../app'),dir=path.resolve(process.argv[2]||'rime-ice'),corpus={};let rows=0;
for(const name of ['8105','base','ext'])for(const line of fs.readFileSync(dir+'/'+name+'.dict.yaml','utf8').split(/\r?\n/)){
 const [text,pinyin,weight]=line.split('\t');if(!text||!pinyin||!/^([a-z]+ )*[a-z]+$/.test(pinyin)||!/^[\u3400-\u9fff]{1,8}$/.test(text)||pinyin.split(' ').length!==text.length)continue;
 const list=corpus[pinyin]||(corpus[pinyin]=[]);if(!list.some(v=>v[0]===text))list.push([text,Number(weight)||1]);rows++;
}
for(const key in corpus)corpus[key].sort((a,b)=>b[1]-a[1]);
fs.writeFileSync(base+'/Edge/engine/corpus.json',JSON.stringify(corpus));const p=JSON.parse(fs.readFileSync(base+'/corpus-provenance.json'));p.rows=rows;p.keys=Object.keys(corpus).length;fs.writeFileSync(base+'/corpus-provenance.json',JSON.stringify(p,null,2));console.log(p);
