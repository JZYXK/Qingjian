using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;

sealed class SelectionReply {internal int exitCode;internal bool timedOut;internal string status,preview;}
static class SelectionWorker {
 internal static bool Authenticate(){
  try{string line=Console.In.ReadLine();if(line==null||line.Length>1024)return false;var request=Local.Json.Deserialize<Dictionary<string,object>>(line);if(Convert.ToString(request["token"])!=Convert.ToString(Local.Config["token"]))return false;
   using(var parent=Process.GetProcessById(Convert.ToInt32(request["parent"])))if(parent.HasExited)return false;
   Target=new IntPtr(Convert.ToInt64(request["window"]));return true;
  }catch(Exception error){Emit("worker-auth-"+error.GetType().Name);return false;}
 }
 internal static IntPtr Target;
 internal static void Emit(string status,string preview=null){try{Console.WriteLine(Local.Json.Serialize(new{status=status,preview=preview}));Console.Out.Flush();}catch{}}
 internal static async Task<SelectionReply> Run(IntPtr job,IntPtr target,Func<bool> canceled=null,string qa=null,int timeout=10000){
  var result=new SelectionReply();
  using(var process=new Process()){
   process.StartInfo=new ProcessStartInfo(Local.Executable,"/convert-once"+(qa==null?"":" "+qa)){WorkingDirectory=Local.Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
   process.Start();if(job!=IntPtr.Zero&&!Native.AssignProcessToJobObject(job,process.Handle)){process.Kill();throw new IOException("转换进程退出保护失败");}
   byte[] request=Encoding.UTF8.GetBytes(Local.Json.Serialize(new{token=Local.Config["token"],parent=Process.GetCurrentProcess().Id,window=target.ToInt64()})+"\n");await process.StandardInput.BaseStream.WriteAsync(request,0,request.Length);process.StandardInput.Close();
   var output=ReadBounded(process.StandardOutput);var errors=ReadBounded(process.StandardError);var wait=Task.Run(()=>process.WaitForExit());var clock=Stopwatch.StartNew();
   while(!wait.IsCompleted){if(clock.ElapsedMilliseconds>timeout||(canceled!=null&&canceled())){result.timedOut=clock.ElapsedMilliseconds>timeout;try{process.Kill();}catch{}break;}await Task.WhenAny(wait,Task.Delay(80));}
   await wait;result.exitCode=process.ExitCode;string records=await output;await errors;
   foreach(string record in records.Split('\n'))try{if(String.IsNullOrWhiteSpace(record))continue;var item=Local.Json.Deserialize<Dictionary<string,object>>(record);object value;if(item.TryGetValue("status",out value))result.status=Convert.ToString(value);if(item.TryGetValue("preview",out value)&&value is string)result.preview=(string)value;}catch{}
  }return result;
 }
 static async Task<string> ReadBounded(StreamReader reader){var text=new StringBuilder();var buffer=new char[512];int count;while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0)if(text.Length<16384)text.Append(buffer,0,Math.Min(count,16384-text.Length));return text.ToString();}
 internal static void CheckIsolation(){
  var job=Native.CreateKillJob();try{var crashed=Run(job,IntPtr.Zero,null,"--qa-exit",2000).GetAwaiter().GetResult();var stuck=Run(job,IntPtr.Zero,null,"--qa-hang",700).GetAwaiter().GetResult();bool ok=crashed.exitCode==91&&stuck.timedOut;File.WriteAllText(Path.Combine(Local.Root,"isolation-check.json"),Local.Json.Serialize(new{passed=ok,childExit=crashed.exitCode,childStatus=crashed.status,timeoutContained=stuck.timedOut,ownerAlive=true,scope="Real separate child processes; controlled nonzero exit and actual timeout/kill; no UI Automation or clipboard in tray"}));Environment.ExitCode=ok?0:1;}finally{Native.CloseHandle(job);}
 }
}
