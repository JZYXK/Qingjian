using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;

static class Local {
 internal static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
 internal static string Executable { get {return System.Reflection.Assembly.GetEntryAssembly().Location;} }
 internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafePipeHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 internal static Dictionary<string,object> Config { get { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"local-config.json"))); } }
 internal static string Pipe { get {return Environment.GetEnvironmentVariable("QJ_TEST_PIPE")??Convert.ToString(Config["pipe"]);} }
 internal static void EnsureConfig(){
  string file=Path.Combine(Root,"local-config.json");if(File.Exists(file))return;
  var manifest=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"Edge","manifest.json")));byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(Convert.FromBase64String(Convert.ToString(manifest["key"])));var extension=new StringBuilder();for(int i=0;i<16;i++){extension.Append((char)('a'+(hash[i]>>4)));extension.Append((char)('a'+(hash[i]&15)));}
  var random=new byte[48];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(random);string hex=BitConverter.ToString(random).Replace("-","").ToLowerInvariant();
  var config=new{pipe="qingjian_v2_"+hex.Substring(0,32),token=hex.Substring(32),extensionId=extension.ToString()};var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,AccessControlType.Allow));acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,AccessControlType.Allow));
  byte[] data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(config));string temp=Path.Combine(Root,".config-"+Guid.NewGuid().ToString("N")+".tmp");
  try{using(var output=new FileStream(temp,FileMode.CreateNew,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,acl))output.Write(data,0,data.Length);File.Move(temp,file);}catch(IOException){if(!File.Exists(file))throw;}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 internal static void PruneOldExecutables(){
  string bridge="";try{bridge=Convert.ToString(Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"com.qingjian.local.json")))["path"]);}catch{}
  foreach(string name in new[]{"Qingjian.exe","Qingjian040.exe","Qingjian050.exe","Qingjian051.exe","Qingjian052.exe","Qingjian053.exe","QingjianBridge.exe","QingjianBridge051.exe","QingjianBridge052.exe","QingjianBridge053.exe","清简.previous.exe"})try{string file=Path.Combine(Root,name);if(String.Equals(file,Executable,StringComparison.OrdinalIgnoreCase)||String.Equals(file,bridge,StringComparison.OrdinalIgnoreCase))continue;if(File.Exists(file)&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal static void ProtectPrivateFiles(){
  var user=WindowsIdentity.GetCurrent().User;var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
  string parent=Path.Combine(Root,"user-data");if(Directory.Exists(parent)&&(File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("隐私目录不可使用目录链接");
  foreach(string file in new[]{"local-config.json","user-data/confirmed-choices.json"}){
   string target=Path.Combine(Root,file);if(!File.Exists(target))continue;if((File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("隐私文件不可使用目录链接");
   var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);acl.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,AccessControlType.Allow));acl.AddAccessRule(new FileSystemAccessRule(system,FileSystemRights.FullControl,AccessControlType.Allow));File.SetAccessControl(target,acl);
  }
  string folder=Path.Combine(Root,"user-data");if(!Directory.Exists(folder))Directory.CreateDirectory(folder);if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("隐私目录不可使用目录链接");
  var rules=new DirectorySecurity();rules.SetAccessRuleProtection(true,false);foreach(var sid in new[]{user,system})rules.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(folder,rules);
 }
 internal static string Rpc(Dictionary<string,object> request,int timeoutMs=65000) {
  var cfg=Config;var serializer=new JavaScriptSerializer();var message=new Dictionary<string,object>(request);message["token"]=cfg["token"];
  string pipe=Pipe;
  using(var handle=CreateFile("\\\\.\\pipe\\"+pipe,0xC0000000,0,IntPtr.Zero,3,0x40100000,IntPtr.Zero)){
   if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  using(var client=new NamedPipeClientStream(PipeDirection.InOut,true,true,handle)){
   var watch=Stopwatch.StartNew();
   byte[] bytes=Encoding.UTF8.GetBytes(serializer.Serialize(message)+"\n");client.Write(bytes,0,bytes.Length);client.Flush();
   using(var reader=new StreamReader(client,new UTF8Encoding(false,true),false,1024,true)){
    var output=new StringBuilder();var chunk=new char[1024];
    while(true){var task=reader.ReadAsync(chunk,0,chunk.Length);int remaining=timeoutMs-(int)watch.ElapsedMilliseconds;if(remaining<=0||!task.Wait(remaining))throw new TimeoutException("本地引擎超时");int count=task.Result;if(count==0)throw new IOException("本地引擎未连接");output.Append(chunk,0,count);if(output.Length>65536)throw new IOException("本地响应过长");string result=output.ToString();int end=result.IndexOf('\n');if(end>=0)return result.Substring(0,end);}
   }
  }}
 }
 internal static string Quote(string value) { return "\""+value.Replace("\"","")+"\""; }
}
static class RuntimeLog {
 internal static int UiErrors;
 static readonly object gate=new object();
 internal static void Write(string stage,Exception error){try{lock(gate){string file=Path.Combine(Local.Root,String.IsNullOrEmpty(Environment.GetEnvironmentVariable("QJ_TEST_PIPE"))?"runtime-events.jsonl":"runtime-events-test.jsonl");string line=new JavaScriptSerializer().Serialize(new{time=DateTime.UtcNow.ToString("o"),stage=stage,type=error==null?"none":error.GetType().Name})+"\n";if(File.Exists(file)&&new FileInfo(file).Length>16384)File.WriteAllText(file,line);else File.AppendAllText(file,line);}}catch{}}
}
