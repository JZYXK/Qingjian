'use strict';
const fs=require('fs'),path=require('path'),crypto=require('crypto'),readline=require('readline'),{pipeline}=require('stream/promises');
const root=__dirname,folder=path.join(root,'models'),file=path.join(folder,'qwen3-4b-instruct-q4_k_m.gguf');
const expected='3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597',bytes=2497281120;
const url='https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/a06e946bb6b655725eafa393f4a9745d460374c9/Qwen3-4B-Instruct-2507-Q4_K_M.gguf';
async function digest(file){const hash=crypto.createHash('sha256');for await(const chunk of fs.createReadStream(file))hash.update(chunk);return hash.digest('hex')}
(async()=>{
 if(JSON.parse(fs.readFileSync(path.join(root,'package-marker.json'))).product!=='Qingjian-V2')throw Error('清简目录标记不正确');
 if(fs.existsSync(folder)&&fs.lstatSync(folder).isSymbolicLink())throw Error('模型目录不能是链接');fs.mkdirSync(folder,{recursive:true});
 if(fs.existsSync(file)){if(fs.lstatSync(file).isSymbolicLink()||await digest(file)!==expected)throw Error('现有模型校验不符，请先自行备份并检查，未覆盖');console.log('本地模型已存在，SHA-256 校验通过。');return}
 console.log('可选本地 AI：Qwen3 4B，约2.5GB。此操作会连接 Hugging Face 下载公开模型，不发送输入内容。词库功能无需下载。');
 const input=readline.createInterface({input:process.stdin,output:process.stdout});const answer=await new Promise(ok=>input.question('确认下载？输入 y 继续，其他键取消：',ok));input.close();if(answer.trim().toLowerCase()!=='y'){console.log('已取消。');return}
 const temp=file+'.'+crypto.randomBytes(6).toString('hex')+'.part';
 try{const response=await fetch(url,{signal:AbortSignal.timeout(3600000)});if(!response.ok)throw Error('下载失败 HTTP '+response.status);const hash=crypto.createHash('sha256');let size=0,last=0;
  await pipeline(response.body,async function*(source){for await(const chunk of source){size+=chunk.length;if(size>bytes)throw Error('下载大小异常');hash.update(chunk);if(size-last>256*1024*1024){console.log(Math.round(size/1048576)+' MB');last=size}yield chunk}},fs.createWriteStream(temp,{flags:'wx'}));
  if(size!==bytes||hash.digest('hex')!==expected)throw Error('校验失败，模型未启用');await fs.promises.rename(temp,file);console.log('下载完成，SHA-256 校验通过。重开清简弹窗查看本地 AI 状态。');
 }finally{await fs.promises.unlink(temp).catch(()=>{})}
})().catch(error=>{console.error(error.message);process.exitCode=1});
