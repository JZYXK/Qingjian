using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Microsoft.Win32;
[assembly:System.Reflection.AssemblyTitle("清简")]
[assembly:System.Reflection.AssemblyProduct("清简")]
[assembly:System.Reflection.AssemblyVersion("0.5.4.3")]

class Assistant {
 internal static readonly Stopwatch StartupWatch=Stopwatch.StartNew();
 static Assistant(){}
 [STAThread] static void Main(string[] args) {
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
  Application.ThreadException+=(sender,eventArgs)=>{Interlocked.Increment(ref RuntimeLog.UiErrors);RuntimeLog.Write("ui-error",eventArgs.Exception);};
  AppDomain.CurrentDomain.UnhandledException+=(sender,eventArgs)=>RuntimeLog.Write("fatal",eventArgs.ExceptionObject as Exception);
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  if(args.Length>0&&args[0]=="/layoutcheck"){FallbackWindow.CheckLayout();return;}
  Local.EnsureConfig();
  if(args.Length>0&&args[0]=="/convert-once"){
   Console.SetIn(new StreamReader(Console.OpenStandardInput(),System.Text.Encoding.UTF8));Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true});
   if(!SelectionWorker.Authenticate()){Environment.ExitCode=78;return;}
   bool qa=(Environment.GetEnvironmentVariable("QJ_TEST_PIPE")??"").StartsWith("qingjian_qa_");
   if(qa&&args.Length>1&&args[1]=="--qa-exit"){Environment.ExitCode=91;return;}
   if(qa&&args.Length>1&&args[1]=="--qa-hang"){Thread.Sleep(30000);return;}
   Application.Run(new Tray(false,1200,false,true));return;
  }
  if(args.Length>0&&args[0]=="/isolationcheck"){
   if((Environment.GetEnvironmentVariable("QJ_TEST_PIPE")??"").StartsWith("qingjian_qa_"))SelectionWorker.CheckIsolation();else Environment.ExitCode=2;return;
  }
  if(args.Length>0&&args[0]=="/bootstrapcheck"){var config=Local.Config;File.WriteAllText(Path.Combine(Local.Root,"bootstrap-check.json"),Local.Json.Serialize(new{created=File.Exists(Path.Combine(Local.Root,"local-config.json")),extensionId=config["extensionId"],tokenLength=Convert.ToString(config["token"]).Length}));return;}
  if(args.Length>0&&args[0]=="/update"){UpdateInstalled();return;}
  if(args.Length>0&&args[0]=="/diagnose"){Diagnose();Environment.Exit(0);return;}
  if(args.Length>0&&args[0]=="/nativeeditcheck"){Native.CheckEditCompatibility();Environment.Exit(0);return;}
  bool test=args.Length>0&&(args[0]=="/smoketest"||args[0]=="/lifecycletest"||args[0]=="/readycheck"||args[0]=="/resiliencecheck");
  if(test&&!(Environment.GetEnvironmentVariable("QJ_TEST_PIPE")??"").StartsWith("qingjian_qa_")){Environment.ExitCode=2;return;}
  if(!test){EnsureIntegration();StartupSettings.FirstRunChoice();}
  bool fresh;using(var mutex=new Mutex(true,"Local\\QingjianV2_"+Local.Pipe+(test?"_test":""),out fresh)){
   if(!fresh){
    bool same=false;try{var r=Local.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Local.Root,"running-version.json")));same=Convert.ToString(r["version"])=="0.5.4.3";}catch{}
    if(same){MessageBox.Show("清简已在运行。右键任务栏托盘图标可测试或退出。","清简");return;}
    bool acquired=false;try{using(var quit=EventWaitHandle.OpenExisting("Local\\"+Local.Pipe+"_exit",System.Security.AccessControl.EventWaitHandleRights.Modify))quit.Set();acquired=mutex.WaitOne(3500);}catch(AbandonedMutexException){acquired=true;}catch{}
    if(!acquired){MessageBox.Show("请先在托盘退出旧版清简，再双击桌面图标启动新版。","清简");return;}
   }
   if(!test)File.WriteAllText(Path.Combine(Local.Root,"running-version.json"),Local.Json.Serialize(new{version="0.5.4.3",pid=Process.GetCurrentProcess().Id}));
   bool life=args.Length>0&&args[0]=="/lifecycletest";
   bool resilience=test&&args[0]=="/resiliencecheck";
   EventWaitHandle retained=null;
   try{if(resilience)retained=new EventWaitHandle(true,EventResetMode.ManualReset,"Local\\"+Local.Pipe+"_exit");Application.Run(new Tray(test,resilience?12000:life?10000:args.Length>0&&args[0]=="/readycheck"?5000:1200,resilience));}catch(Exception e){RuntimeLog.Write("startup",e);if(!test)MessageBox.Show("启动失败，未修改输入法。\n"+e.Message,"清简");else Environment.ExitCode=1;}finally{if(retained!=null)retained.Dispose();}
  }
 }
 static void EnsureIntegration(){
  try{
   var recordPath=Path.Combine(Local.Root,"install-record.json");
   if(File.Exists(recordPath)){var previous=Local.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(recordPath));object version;if(previous.TryGetValue("version",out version)&&version.ToString()=="0.5.4.3")return;}
   Local.ProtectPrivateFiles();
   var cfg=Local.Config;var manifest=new Dictionary<string,object>{{"name","com.qingjian.local"},{"description","Qingjian offline AI bridge"},{"path",Path.Combine(Local.Root,"QingjianBridge054.exe")},{"type","stdio"},{"allowed_origins",new[]{"chrome-extension://"+cfg["extensionId"]+"/"}}};
   string manifestPath=Path.Combine(Local.Root,"com.qingjian.local.json");File.WriteAllText(manifestPath,Local.Json.Serialize(manifest),new System.Text.UTF8Encoding(false));
   const string keyPath="Software\\Microsoft\\Edge\\NativeMessagingHosts\\com.qingjian.local";
   using(var key=Registry.CurrentUser.CreateSubKey(keyPath))key.SetValue("",manifestPath);
   if(File.Exists(Path.Combine(Local.Root,"startup-choice.json"))){
    var settings=Local.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Local.Root,"startup-choice.json")));
    using(var run=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run",true))if(run!=null&&StartupSettings.Owned(Convert.ToString(run.GetValue("QingjianAssistant")))){
     if(settings.ContainsKey("autoStart")&&(bool)settings["autoStart"])run.SetValue("QingjianAssistant",Local.Quote(Local.Executable));else run.DeleteValue("QingjianAssistant",false);
    }
   }
   var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
   string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"清简.lnk");
   dynamic shortcut=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{link});
   shortcut.TargetPath=Local.Executable;shortcut.WorkingDirectory=Local.Root;shortcut.Description="清简";shortcut.IconLocation=Path.Combine(Local.Root,"assets","qingjian.ico")+",0";shortcut.Save();
   string legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"启动清简助手.lnk");
   if(File.Exists(legacy)){dynamic old=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{legacy});if(StartupSettings.Owned(Local.Quote((string)old.TargetPath)))File.Delete(legacy);}
   File.WriteAllText(recordPath,Local.Json.Serialize(new{root=Local.Root,desktopLink=link,registryKey=keyPath,version="0.5.4.3"}),new System.Text.UTF8Encoding(false));
  }catch(Exception e){MessageBox.Show("桌面快捷方式/Edge 桥接配置未完成："+e.Message+"\n本地助手仍会尝试启动。","清简");}
 }
 static void UpdateInstalled(){
  try{
   string target=Path.Combine(Local.Root,"清简.exe");
   if(String.Equals(Local.Executable,target,StringComparison.OrdinalIgnoreCase))throw new IOException("请使用更新入口");
   try{using(var quit=EventWaitHandle.OpenExisting("Local\\"+Local.Pipe+"_exit",System.Security.AccessControl.EventWaitHandleRights.Modify))quit.Set();}catch(WaitHandleCannotBeOpenedException){}
   string staged=Path.Combine(Local.Root,"清简.update.exe");File.Copy(Local.Executable,staged,true);bool updated=false;
   for(int i=0;i<40&&!updated;i++){try{if(File.Exists(target))File.Replace(staged,target,Path.Combine(Local.Root,"清简.previous.exe"));else File.Move(staged,target);updated=true;}catch(IOException){Thread.Sleep(100);}}
   if(!updated)throw new IOException("旧版仍在运行，请退出托盘清简后重试");
   Process.Start(new ProcessStartInfo(target){WorkingDirectory=Local.Root,UseShellExecute=false});
  }catch(Exception e){MessageBox.Show("更新未完成，旧文件未强制删除。\n"+e.Message,"清简");}
 }
 static void Diagnose(){
  var r=new Dictionary<string,object>();
  r["inputStructSize"]=Marshal.SizeOf(typeof(Native.Input));bool ok=Native.RegisterHotKey(IntPtr.Zero,937,0x4006,0x5A);r["globalHotkey"]=ok;r["hotkeyError"]=ok?0:Marshal.GetLastWin32Error();if(ok)Native.UnregisterHotKey(IntPtr.Zero,937);
  var done=new TaskCompletionSource<bool>();var probe=new Thread(()=>{try{done.TrySetResult(Clipboard.GetDataObject()!=null);}catch{done.TrySetResult(false);}}){IsBackground=true};probe.SetApartmentState(ApartmentState.STA);probe.Start();
  r["clipboardReadable"]=done.Task.Wait(1500)&&done.Task.Result;
  r["desktopApplicationsTested"]=false;
  File.WriteAllText(Path.Combine(Local.Root,"desktop-diagnostic.json"),Local.Json.Serialize(r));
 }
}
static class StartupSettings {
 const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
 internal static bool Owned(string command){return command==Local.Quote(Path.Combine(Local.Root,"Qingjian.exe"))||command==Local.Quote(Path.Combine(Local.Root,"Qingjian040.exe"))||command==Local.Quote(Path.Combine(Local.Root,"Qingjian050.exe"))||command==Local.Quote(Path.Combine(Local.Root,"Qingjian051.exe"))||command==Local.Quote(Path.Combine(Local.Root,"Qingjian052.exe"))||command==Local.Quote(Path.Combine(Local.Root,"Qingjian053.exe"))||command==Local.Quote(Path.Combine(Local.Root,"Qingjian054.exe"))||command==Local.Quote(Path.Combine(Local.Root,"QingjianLocal.exe"))||command==Local.Quote(Path.Combine(Local.Root,"QingjianLocal2.exe"))||command==Local.Quote(Path.Combine(Local.Root,"QingjianLocal3.exe"))||command==Local.Quote(Path.Combine(Local.Root,"清简.exe"));}
 internal static bool Enabled { get { try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){return key!=null&&Convert.ToString(key.GetValue("QingjianAssistant"))==Local.Quote(Local.Executable);}}catch{return false;} } }
 internal static bool Set(bool enabled){
  try{
   using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
    if(enabled)key.SetValue("QingjianAssistant",Local.Quote(Local.Executable));
    else if(Owned(Convert.ToString(key.GetValue("QingjianAssistant"))))key.DeleteValue("QingjianAssistant",false);
   }
   File.WriteAllText(Path.Combine(Local.Root,"startup-choice.json"),Local.Json.Serialize(new{autoStart=enabled}));return true;
  }catch(Exception e){MessageBox.Show("开机启动设置未能保存："+e.Message,"清简");return false;}
 }
 internal static void FirstRunChoice(){
  if(File.Exists(Path.Combine(Local.Root,"startup-choice.json")))return;
  var choice=MessageBox.Show("是否让清简在登录 Windows 后自动启动？\n\n是：开机自启。\n否：保持手动启动，以后双击桌面「清简」即可。\n\n以后可以在托盘菜单中更改。","清简 · 首次启动",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
  Set(choice==DialogResult.Yes);
 }
}
class Tray:ApplicationContext {
 readonly Stopwatch startupWatch=Stopwatch.StartNew();
 readonly Control dispatcher=new Control();readonly NotifyIcon icon;readonly Native.HotkeyWindow window;
 readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer();readonly EventWaitHandle exitEvent;
 Native.WinEventDelegate foregroundCallback;IntPtr hook,job;Process backend;bool registered,paused,busy,closing,ai=true,smokeMode,engineReady,recovering,qaWorkerExited,qaWorkerTimedOut;int engineGeneration,restartCount;DateTime restartWindow=DateTime.UtcNow,retryAfter=DateTime.MinValue;Form fallback;string phase="startup",focusFailure="";string status="正在启动本地引擎…";int lastHotkeyError;
 bool isolated;
 public Tray(bool smoke=false,int smokeDuration=1200,bool resilience=false,bool worker=false){
  isolated=worker;
  smokeMode=smoke;
  var h=dispatcher.Handle;window=new Native.HotkeyWindow(ConvertSelection);
  icon=new NotifyIcon{Icon=LoadIcon(),Text="清简",Visible=!worker};
  if(worker){engineReady=true;dispatcher.BeginInvoke((Action)ConvertSelection);return;}
  var menu=new ContextMenuStrip();menu.Items.Add("状态与用法",null,(sender,e)=>ShowStatus());
  menu.Items.Add("备用转换窗口",null,(sender,e)=>OpenFallback());
  var pause=new ToolStripMenuItem("暂停快捷键"){CheckOnClick=true};pause.CheckedChanged+=(sender,e)=>{paused=pause.Checked;SyncHotkey();};menu.Items.Add(pause);
  var startup=new ToolStripMenuItem("开机自动启动"){Checked=StartupSettings.Enabled,CheckOnClick=true};bool settingStartup=false;
  startup.CheckedChanged+=(sender,e)=>{if(settingStartup)return;if(!StartupSettings.Set(startup.Checked)){settingStartup=true;startup.Checked=StartupSettings.Enabled;settingStartup=false;}};menu.Items.Add(startup);
  menu.Items.Add("退出",null,(sender,e)=>ExitThread());icon.ContextMenuStrip=menu;icon.DoubleClick+=(sender,e)=>ShowStatus();
  bool created;var security=new System.Security.AccessControl.EventWaitHandleSecurity();security.AddAccessRule(new System.Security.AccessControl.EventWaitHandleAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User,System.Security.AccessControl.EventWaitHandleRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));
  exitEvent=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\"+Local.Pipe+"_exit",out created,security);exitEvent.Reset();
  StartBackend();
  foregroundCallback=(a,b,c,d,e,f,g)=>{try{if(!closing&&dispatcher.IsHandleCreated)dispatcher.BeginInvoke((Action)(()=>{try{SyncHotkey();}catch(Exception error){RuntimeLog.Write("foreground",error);}}));}catch(Exception error){RuntimeLog.Write("foreground-dispatch",error);}};hook=Native.SetWinEventHook(3,3,IntPtr.Zero,foregroundCallback,0,0,0);
  poll.Interval=700;poll.Tick+=(sender,e)=>{try{if(exitEvent.WaitOne(0)){ExitThread();return;}if(backend==null||backend.HasExited)RecoverBackend("backend-exited");SyncHotkey();}catch(Exception error){RuntimeLog.Write("poll",error);RecoverBackend("poll-failed");}};poll.Start();SyncHotkey();
  if(smoke){
   File.WriteAllText(Path.Combine(Local.Root,"lifecycle-diagnostic.json"),Local.Json.Serialize(new{backendPid=backend==null?0:backend.Id,jobCreated=job!=IntPtr.Zero,hotkeyRegistered=registered,status=status}));
   if(resilience){
    var workerCheck=new System.Windows.Forms.Timer{Interval=4200};workerCheck.Tick+=async(s,e)=>{workerCheck.Stop();workerCheck.Dispose();var exited=await SelectionWorker.Run(job,IntPtr.Zero,null,"--qa-exit",2000);qaWorkerExited=exited.exitCode==91;var stuck=await SelectionWorker.Run(job,IntPtr.Zero,null,"--qa-hang",700);qaWorkerTimedOut=stuck.timedOut;};workerCheck.Start();
    var fail=new System.Windows.Forms.Timer{Interval=2500};fail.Tick+=(s,e)=>{fail.Stop();fail.Dispose();try{if(backend!=null&&!backend.HasExited)backend.Kill();}catch(Exception error){RuntimeLog.Write("test-kill",error);}};fail.Start();
    var fault=new System.Windows.Forms.Timer{Interval=5000};fault.Tick+=(s,e)=>{fault.Stop();fault.Dispose();dispatcher.BeginInvoke((Action)(()=>{throw new InvalidOperationException("QA");}));};fault.Start();
    var fallbackCheck=new System.Windows.Forms.Timer{Interval=7000};fallbackCheck.Tick+=(s,e)=>{fallbackCheck.Stop();fallbackCheck.Dispose();OpenFallback();((TextBox)fallback.Controls.Find("qjSource",true)[0]).Text="nihao";((Button)fallback.Controls.Find("qjConvert",true)[0]).PerformClick();};fallbackCheck.Start();
   }
   var end=new System.Windows.Forms.Timer{Interval=smokeDuration};end.Tick+=(s,e)=>{end.Stop();end.Dispose();bool fallbackPassed=false;if(fallback!=null)fallbackPassed=((TextBox)fallback.Controls.Find("qjResult",true)[0]).Text=="你好";if(resilience)File.WriteAllText(Path.Combine(Local.Root,"resilience-test.json"),Local.Json.Serialize(new{helperPid=Process.GetCurrentProcess().Id,lifetimeMs=startupWatch.ElapsedMilliseconds,engineReady=engineReady,restarts=restartCount,backendPid=backend==null?0:backend.Id,uiErrors=RuntimeLog.UiErrors,exitSignalReset=!exitEvent.WaitOne(0),menuItems=MenuLabels(menu),fallbackConverted=fallbackPassed,workerExitContained=qaWorkerExited,workerTimeoutContained=qaWorkerTimedOut}));ExitThread();};end.Start();
  }
  else if(paused||lastHotkeyError!=0)dispatcher.BeginInvoke((Action)ShowStatus);

 }
 static string[] MenuLabels(ContextMenuStrip menu){var labels=new List<string>();foreach(ToolStripItem item in menu.Items)labels.Add(item.Text);return labels.ToArray();}
 void StartBackend(){
  if(closing)return;engineReady=false;int generation=++engineGeneration;
  try{
   job=Native.CreateKillJob();
   var start=new ProcessStartInfo(Path.Combine(Local.Root,"runtime\\node.exe"),Local.Quote(Path.Combine(Local.Root,"service.cjs"))+" --parent="+Process.GetCurrentProcess().Id){WorkingDirectory=Local.Root,UseShellExecute=false,CreateNoWindow=true};
   backend=Process.Start(start);if(!Native.AssignProcessToJobObject(job,backend.Handle))throw new IOException("后台退出保护失败");
   phase="starting";status="正在准备词库…";dispatcher.BeginInvoke((Action)(()=>WaitForEngine(generation)));
  }catch(Exception error){RuntimeLog.Write("backend-start",error);if(backend!=null)try{backend.Kill();backend.Dispose();}catch{}backend=null;if(job!=IntPtr.Zero){Native.CloseHandle(job);job=IntPtr.Zero;}Note("后台尚未启动，将自动重试");}
 }
 async void RecoverBackend(string reason){
  if(closing||recovering||DateTime.UtcNow<retryAfter)return;
  if((DateTime.UtcNow-restartWindow).TotalSeconds>60){restartWindow=DateTime.UtcNow;restartCount=0;}
  if(restartCount>=3){retryAfter=restartWindow.AddSeconds(60);Note("后台多次异常，稍后重试；备用窗口仍可打开");return;}
  recovering=true;engineReady=false;++engineGeneration;++restartCount;retryAfter=DateTime.UtcNow.AddSeconds(2);RuntimeLog.Write(reason,null);
  try{
   SyncHotkey();if(job!=IntPtr.Zero){Native.CloseHandle(job);job=IntPtr.Zero;}
   if(backend!=null){backend.Dispose();backend=null;}
   Note("后台正在恢复，托盘保持运行…");await Task.Delay(200);if(!closing)StartBackend();
  }catch(Exception error){RuntimeLog.Write("backend-recovery",error);}finally{recovering=false;}
 }
 void OpenFallback(){
  if(fallback!=null&&!fallback.IsDisposed){fallback.Show();fallback.Activate();return;}
  fallback=new FallbackWindow(async input=>{
   if(!engineReady)throw new InvalidOperationException("backend-not-ready");int generation=engineGeneration;
   var reply=await Task.Run(()=>Local.Rpc(new Dictionary<string,object>{{"text",input},{"force",true},{"ai",ai},{"warmAI",false},{"latencyFirst",true},{"fuzzy",true},{"budgetMs",650}},3500));
   if(closing||generation!=engineGeneration)throw new OperationCanceledException();var body=Local.Json.Deserialize<Dictionary<string,object>>(reply);object output;
   return body.TryGetValue("result",out output)&&output is Dictionary<string,object>?Convert.ToString(((Dictionary<string,object>)output)["text"]):null;
  },LoadIcon());fallback.Show();
 }
 static Icon LoadIcon(){try{string path=Path.Combine(Local.Root,"assets", "qingjian.ico");return File.Exists(path)?new Icon(path):SystemIcons.Information;}catch{return SystemIcons.Information;}}
 async void WaitForEngine(int generation){
  var until=DateTime.UtcNow.AddSeconds(60);
  while(!closing&&generation==engineGeneration&&DateTime.UtcNow<until){
   try{var reply=await Task.Run(()=>Local.Rpc(new Dictionary<string,object>{{"action","status"}},2000));if(closing||generation!=engineGeneration)return;
    var result=Local.Json.Deserialize<Dictionary<string,object>>(reply);object ready,error;
    if(!result.ContainsKey("servicePid")||backend==null||Convert.ToInt32(result["servicePid"])!=backend.Id){await Task.Delay(60);continue;}
    if(result.ContainsKey("ok")&&Convert.ToBoolean(result["ok"])&&result.TryGetValue("dictionaryReady",out ready)&&ready is bool&&(bool)ready){
     engineReady=true;phase="ready";Note("词库已就绪（桌面转换不等待AI）");SyncHotkey();
     try{File.WriteAllText(Path.Combine(Local.Root,smokeMode?"readiness-diagnostic-test.json":"readiness-diagnostic.json"),Local.Json.Serialize(new{helperVersion="0.5.4.3",helperPid=Process.GetCurrentProcess().Id,dictionaryReady=true,backendPid=backend==null?0:backend.Id,hotkeyRegistered=registered,helperReadyMs=startupWatch.ElapsedMilliseconds,totalReadyMs=Assistant.StartupWatch.ElapsedMilliseconds,dictionaryMs=result.ContainsKey("dictionaryMs")?result["dictionaryMs"]:null,aiLoaded=result.ContainsKey("aiLoaded")&&Convert.ToBoolean(result["aiLoaded"])}));}catch{}
     if(!smokeMode){bool blocked=ForegroundProcess()!="msedge"&&!registered&&!paused;icon.ShowBalloonTip(2500,"清简",blocked?"词库已就绪，但快捷键不可用。双击托盘查看状态。":"词库已就绪，选中拼音按 Ctrl+Shift+Z。",blocked?ToolTipIcon.Warning:ToolTipIcon.Info);}return;
    }
    if(result.TryGetValue("dictionaryError",out error)&&!String.IsNullOrEmpty(Convert.ToString(error))){phase="dictionary-failed";RecoverBackend("dictionary-failed");return;}
   }catch{}
   if(closing||generation!=engineGeneration)return;phase="startup-wait";status="正在等待后台和词库就绪…";SyncHotkey();await Task.Delay(60);
  }
  if(!closing&&generation==engineGeneration){phase="startup-timeout";RecoverBackend("readiness-timeout");}
 }
 string ForegroundProcess(){try{uint id;Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out id);using(var process=Process.GetProcessById((int)id))return process.ProcessName.ToLowerInvariant();}catch{return "";}}
 void SyncHotkey(){
  if(closing)return;bool want=engineReady&&!paused&&ForegroundProcess()!="msedge";
  if(!want){if(registered)Native.UnregisterHotKey(window.Handle,936);registered=false;lastHotkeyError=0;}
  if(want&&!registered){int priorError=lastHotkeyError;registered=Native.RegisterHotKey(window.Handle,936,0x4006,0x5A);lastHotkeyError=registered?0:Marshal.GetLastWin32Error();if(!registered&&lastHotkeyError!=priorError)Note("全局快捷键注册失败，Windows 错误 "+lastHotkeyError);}
  icon.Text=!engineReady?"清简：词库尚未就绪":paused?"清简：已暂停":lastHotkeyError!=0?"清简：快捷键不可用（双击查看）":"清简：Ctrl+Shift+Z";
 }
 void ShowStatus(){
  string key=paused?"已暂停":ForegroundProcess()=="msedge"?"Edge 内由扩展接管快捷键":!engineReady?"词库尚未就绪，尚未占用快捷键":registered?"Ctrl+Shift+Z 已注册":"快捷键注册失败，Windows 错误码 "+lastHotkeyError;
  MessageBox.Show("清简 · 本地修复 0.5.4.3\n"+status+"\n"+key+"\n\n桌面：双击清简，等待托盘提示就绪，选中拼音按 Ctrl+Shift+Z。\n浏览器插件可以独立提供词库混输；本地AI需要桌面清简，启动顺序不限。\n微信/Codex若无法验证选区：打开备用转换窗口，粘贴拼音，转换后复制结果自行粘贴。\n\n不会改变Windows输入法。开机自启："+(StartupSettings.Enabled?"已开启":"未开启")+"。\n后台异常会有限重试；诊断只记录阶段和错误类型，不记录输入。","清简");
 }
 void Note(string reason){status=reason;if(isolated){SelectionWorker.Emit(reason);return;}if(smokeMode||closing)return;try{File.WriteAllText(Path.Combine(Local.Root,"assistant-status.json"),Local.Json.Serialize(new{time=DateTime.UtcNow.ToString("o"),status=reason,hotkeyRegistered=registered,hotkeyError=lastHotkeyError,phase=phase,foregroundProcess=ForegroundProcess(),dictionaryReady=engineReady}));}catch{}}
 static uint LastInput(){var t=new Native.LastInputInfo();t.cbSize=(uint)Marshal.SizeOf(t);return Native.GetLastInputInfo(ref t)?t.dwTime:0;}
 static IDataObject BackupClipboard(){
  var src=Clipboard.GetDataObject();var dst=new DataObject();if(src==null)return dst;
  var standard=new HashSet<string>{DataFormats.Text,DataFormats.UnicodeText,DataFormats.OemText,DataFormats.Html,DataFormats.Rtf,DataFormats.CommaSeparatedValue,DataFormats.FileDrop,DataFormats.Bitmap};
  string[] formats=src.GetFormats(false);if(formats.Length>64)throw new IOException("剪贴板格式过多，原样保留");long total=0;
  foreach(string fmt in formats){
   // Never ask OLE/.NET to deserialize arbitrary custom clipboard objects.
   // Unknown HGLOBAL formats are copied as bounded opaque bytes instead.
   // OLE may own the clipboard while its IDataObject is being read. Opening it
   // again through Win32 even for UnicodeText caused valid copies to abort.
   // Read only known text/file/image formats via OLE; unknown formats remain
   // bounded opaque bytes and are never deserialized.
   object data=standard.Contains(fmt)?src.GetData(fmt,false):new MemoryStream(Native.RawClipboardFormat(DataFormats.GetFormat(fmt).Id));
   var text=data as string;if(text!=null)total+=(long)text.Length*2;
   var files=data as string[];if(files!=null){foreach(string file in files)total+=(long)file.Length*2;data=files.Clone();}
   var stream=data as MemoryStream;if(stream!=null){total+=stream.Length;data=new MemoryStream(stream.ToArray());}
   if(total>16*1024*1024)throw new IOException("剪贴板过大，原样保留");
   var bitmap=data as Bitmap;if(bitmap!=null){if((long)bitmap.Width*bitmap.Height>4*1024*1024)throw new IOException("图片剪贴板过大，保留原剪贴板");data=new Bitmap(bitmap);}
   if(data!=null)dst.SetData(fmt,false,data);
  }return dst;
 }
 async Task<string> CopySelected(){
  uint seq=Native.GetClipboardSequenceNumber();if(!Native.Chord(0x43)){Note("Windows 拒绝复制，可能存在权限差异");return null;}
  for(int i=0;i<20;i++){await Task.Delay(25);if(closing)return null;if(Native.GetClipboardSequenceNumber()!=seq)return Clipboard.ContainsText()?Clipboard.GetText():null;}return null;
 }
 Task clipboardProbe;string clipboardFailure="";
 async Task<IDataObject> BackupAsync(){
  if(clipboardProbe!=null&&!clipboardProbe.IsCompleted)return null;
  clipboardFailure="";var done=new TaskCompletionSource<IDataObject>();var t=new Thread(()=>{try{done.TrySetResult(BackupClipboard());}catch(Exception error){RuntimeLog.Write("clipboard-backup",error);var reasons=new HashSet<string>{"剪贴板格式过多，原样保留","剪贴板过大，原样保留","图片剪贴板过大，保留原剪贴板","单个剪贴板格式超过16MB","剪贴板内存无法读取","剪贴板数据流过大或无效","该剪贴板格式不能安全复制"};clipboardFailure=reasons.Contains(error.Message)?error.Message:"格式访问失败，代码0x"+Marshal.GetHRForException(error).ToString("X8");done.TrySetResult(null);}}){IsBackground=true};t.SetApartmentState(ApartmentState.STA);t.Start();
  clipboardProbe=done.Task;if(await Task.WhenAny(done.Task,Task.Delay(1000))==done.Task)return await done.Task;clipboardFailure="剪贴板响应超时";return null;
 }
 bool SafeFocus(out AutomationElement focus,out TextPatternRange range,out string before,out string after,out bool readOnly){
  var el=AutomationElement.FocusedElement;
  if(SafeElement(el,false,out focus,out range,out before,out after,out readOnly))return true;
  string name=ForegroundProcess();if(el==null||el.Current.IsPassword||!el.Current.IsEnabled||(name!="codex"&&name!="chatgpt"))return false;
  for(int i=0;i<2;i++){var parent=TreeWalker.ControlViewWalker.GetParent(el);if(parent==null||parent.Current.ProcessId!=el.Current.ProcessId)break;el=parent;if(SafeElement(el,true,out focus,out range,out before,out after,out readOnly))return true;}return false;
 }
 bool SafeElement(AutomationElement el,bool adapter,out AutomationElement focus,out TextPatternRange range,out string before,out string after,out bool readOnly){
  focus=null;range=null;before=after="";readOnly=false;focusFailure="uia-no-verifiable-selection";
  try{
   if(el==null){focusFailure="uia-no-focused-element";return false;}if(el.Current.IsPassword){focusFailure="password-blocked";return false;}if(!el.Current.IsEnabled){focusFailure="disabled-control";return false;}
   object value;bool writable=el.TryGetCurrentPattern(ValuePattern.Pattern,out value);
   if(writable)readOnly=((ValuePattern)value).Current.IsReadOnly;else readOnly=true;
   bool standard=el.Current.ControlType==ControlType.Edit||el.Current.ControlType==ControlType.Document||el.Current.ControlType==ControlType.Text||writable&&el.Current.IsKeyboardFocusable;

   focus=el;
   if(el.TryGetCurrentPattern(TextPattern.Pattern,out value)){
    var selections=((TextPattern)value).GetSelection();if(selections.Length!=1||selections[0].GetText(161).Length==0){focusFailure="uia-no-explicit-selection";return false;}
    range=selections[0].Clone();object attribute=range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);if(attribute is bool)readOnly=(writable&&readOnly)||(bool)attribute;
   }
   
   return range!=null&&(standard||adapter&&el.Current.IsKeyboardFocusable&&!readOnly)||writable&&!readOnly;
  }catch(Exception e){focusFailure="uia-"+e.GetType().Name;return false;}
 }
 Task providerProbe;
 class FocusSnapshot{internal AutomationElement element,focused;internal TextPatternRange range;internal string before,after;internal Native.EditSelection edit;internal bool readOnly;}
 async Task<FocusSnapshot> FocusAsync(){
  if(providerProbe!=null&&!providerProbe.IsCompleted){focusFailure="provider-busy";return null;}
  var task=Task.Run(()=>{try{var edit=Native.ForegroundEditSelection();if(edit!=null&&edit.end<=4096)return new FocusSnapshot{edit=edit,before="",after="",readOnly=edit.readOnly};var focused=AutomationElement.FocusedElement;AutomationElement el;TextPatternRange r;string b,a;bool readOnly;return SafeFocus(out el,out r,out b,out a,out readOnly)?new FocusSnapshot{element=el,focused=focused,range=r,before=b,after=a,readOnly=readOnly}:null;}catch{focusFailure="uia-provider-unavailable";return null;}});
  providerProbe=task;
  if(await Task.WhenAny(task,Task.Delay(1200))==task){var result=await task;if(result!=null)return result;}else focusFailure="focus-check-timeout";
  return null;
 }
 async Task<bool> SameFocusAsync(FocusSnapshot prior,string text){
  if(providerProbe!=null&&!providerProbe.IsCompleted)return false;
  var task=Task.Run(()=>{AutomationElement now;TextPatternRange range;string b,a;
   
   if(prior.edit!=null){var current=Native.ForegroundEditSelection();return current!=null&&current.handle==prior.edit.handle&&current.start==prior.edit.start&&current.end==prior.edit.end&&current.readOnly==prior.readOnly;}
   bool readOnly;if(!Automation.Compare(prior.focused,AutomationElement.FocusedElement)||!SafeFocus(out now,out range,out b,out a,out readOnly)||readOnly!=prior.readOnly||!Automation.Compare(prior.element,now))return false;
   if(prior.range==null)return range==null;
   return range!=null&&range.GetText(161)==text&&prior.range.CompareEndpoints(TextPatternRangeEndpoint.Start,range,TextPatternRangeEndpoint.Start)==0&&prior.range.CompareEndpoints(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.End)==0;
  });providerProbe=task;return await Task.WhenAny(task,Task.Delay(1200))==task&&await task;
 }
 async Task<string> ConvertText(string selected){
  var reply=await Task.Run(()=>Local.Rpc(new Dictionary<string,object>{{"text",selected},{"force",true},{"ai",true},{"warmAI",false},{"latencyFirst",true},{"fuzzy",true},{"budgetMs",650}},3500));
  var result=Local.Json.Deserialize<Dictionary<string,object>>(reply);object output;return result.TryGetValue("result",out output)&&output is Dictionary<string,object>?Convert.ToString(((Dictionary<string,object>)output)["text"]):null;
 }
 static string ComparableSelection(string text){return (text??"").Replace("\r\n","\n").Replace('\r','\n').Replace('\u00a0',' ').TrimEnd('\n');}
 async Task ConvertClipboardSelection(FocusSnapshot snapshot,IntPtr foreground){
  // The legacy clipboard adapter runs only in the disposable selection child.
  // In particular, do not move UI Automation or clipboard access into the tray.
  IDataObject backup=null;uint owned=0;bool restore=false;var clock=Stopwatch.StartNew();
  try{
   Note("正在读取选区（兼容模式）");uint sequence=Native.GetClipboardSequenceNumber();backup=await BackupAsync();
   if(backup==null){Note("剪贴板备份取消（"+clipboardFailure+"），未修改原文");return;}
   if(Native.GetClipboardSequenceNumber()!=sequence){Note("备份期间剪贴板发生变化，未修改原文");return;}
   if(Native.GetForegroundWindow()!=foreground){Note("备份期间窗口切换，未修改原文");return;}
   string marker=Guid.NewGuid().ToString();Clipboard.SetText(marker);owned=Native.GetClipboardSequenceNumber();restore=true;
   string selected=await CopySelected();owned=Native.GetClipboardSequenceNumber();
   if(String.IsNullOrWhiteSpace(selected)||selected==marker||selected.Length>160){Note("未复制到拼音选区，原文保留");return;}
   string core=selected.Trim(' ','\t','\r','\n','\u00a0');
   if(!WordSelection.Valid(core.Replace('\u00a0',' '))){Note("选区不是单段拼音，原文保留");return;}
   string rangeText=snapshot.range==null?selected:snapshot.range.GetText(161);
   if(snapshot.range!=null&&ComparableSelection(rangeText)!=ComparableSelection(selected)){Note("复制内容与选区不一致，已取消");return;}
   if(Native.GetForegroundWindow()!=foreground||!await SameFocusAsync(snapshot,rangeText)||Native.GetClipboardSequenceNumber()!=owned){Note("选区或剪贴板已变化，已取消");return;}
   Clipboard.SetDataObject(backup,true);owned=Native.GetClipboardSequenceNumber();restore=false;
   uint activity=Native.Activity();bool strict=snapshot.range==null;
   string converted=await ConvertText(core.Replace('\u00a0',' '));
   if(!WordSelection.ValidResult(converted)){Note("未获得转换结果，原文保留");return;}
   if(Native.GetForegroundWindow()!=foreground||Native.GetClipboardSequenceNumber()!=owned||strict&&(activity==0||Native.Activity()!=activity)||!await SameFocusAsync(snapshot,rangeText)){Note("转换期间选区或输入变化，原文保留");return;}
   if(snapshot.readOnly){ShowReadOnlyResult(converted);return;}
   restore=true;string verify=await CopySelected();owned=Native.GetClipboardSequenceNumber();
   if(verify!=selected||Native.GetForegroundWindow()!=foreground||!await SameFocusAsync(snapshot,rangeText)||Native.GetClipboardSequenceNumber()!=owned){Note("再次核对选区失败，原文保留");return;}
   int offset=selected.IndexOf(core,StringComparison.Ordinal);string replacement=selected.Substring(0,offset)+converted+selected.Substring(offset+core.Length);
   Clipboard.SetText(replacement);owned=Native.GetClipboardSequenceNumber();
   if(Native.GetForegroundWindow()!=foreground){Note("窗口已切换，已取消粘贴");return;}
   if(!Native.Chord(0x56)){Note("系统未接受粘贴，请检查软件权限");return;}
   await Task.Delay(220);Note("兼容转换完成（"+clock.ElapsedMilliseconds+"毫秒）");
  }finally{if(restore&&backup!=null)try{if(Native.GetClipboardSequenceNumber()==owned)Clipboard.SetDataObject(backup,true);}catch{Note("转换已停止，但剪贴板恢复失败");}}
 }
 async void ConvertSelection(){
  if(busy||paused||closing||ForegroundProcess()=="msedge")return;
  if(!engineReady){Note("词库准备中，请稍候");return;}
  if(new HashSet<string>{"windowsterminal","cmd","powershell","pwsh","conhost","mintty","wt"}.Contains(ForegroundProcess())){Note("终端窗口不处理");if(isolated)ExitThread();return;}
  busy=true;int generation=engineGeneration;IntPtr foreground=Native.GetForegroundWindow();
  try{
   if(!isolated){
    Note("已收到快捷键，正在独立进程中转换");
    var result=await SelectionWorker.Run(job,foreground,()=>closing||paused||generation!=engineGeneration||Native.GetForegroundWindow()!=foreground);
    if(closing)return;
    if(result.timedOut){RuntimeLog.Write("selection-timeout",null);Note("输入框响应超时，转换已取消；托盘仍在运行");return;}
    if(result.exitCode!=0){RuntimeLog.Write("selection-worker-exit",null);Note("输入框兼容进程已结束，托盘仍在运行；可重试或使用备用窗口");return;}
    Note(result.status??"转换已取消，原文保留");if(WordSelection.ValidResult(result.preview))ShowReadOnlyResult(result.preview);return;
   }
   if(foreground!=SelectionWorker.Target)return;
   for(int i=0;i<150&&(Native.GetAsyncKeyState(0x11)<0||Native.GetAsyncKeyState(0x10)<0||Native.GetAsyncKeyState(0x5A)<0);i++)await Task.Delay(10);
   if(Native.GetAsyncKeyState(0x11)<0||Native.GetAsyncKeyState(0x10)<0||Native.GetAsyncKeyState(0x5A)<0)return;
   // Word exposes its own selected range, without the generic UIA provider.
   if(ForegroundProcess()=="winword"){await WordSelection.TryConvert(ConvertText,Note,ShowReadOnlyResult);return;}
   phase="focus-check";Note("正在检查可编辑选区");var snapshot=await FocusAsync();
   if(snapshot==null||Native.GetForegroundWindow()!=foreground){Note("无法确认可编辑选区（"+focusFailure+"），请使用备用窗口");return;}
   if(snapshot.edit==null){await ConvertClipboardSelection(snapshot,foreground);return;}
   string selected=snapshot.edit!=null?Native.ReadSelectedText(snapshot.edit):snapshot.range.GetText(161);
   if(!WordSelection.Valid(selected)){Note("请选中一段不超过160字符的拼音，跨段内容暂不处理");return;}
   uint activity=Native.Activity();phase="convert";string converted=await ConvertText(selected);
   if(!WordSelection.ValidResult(converted)||Native.GetForegroundWindow()!=foreground||activity==0||Native.Activity()!=activity||!await SameFocusAsync(snapshot,selected)){Note("选区或输入已变化，原文保留");return;}
   if(snapshot.readOnly){ShowReadOnlyResult(converted);return;}
   phase="replace";bool replaced=snapshot.edit!=null?Native.ReplaceEditSelection(snapshot.edit,selected,converted):Native.TypeUnicode(converted);
   Note(replaced?"选区转换完成，剪贴板未改动":"无法确认替换完成，请检查原文；受限输入框可用备用窗口");
  }catch(Exception error){RuntimeLog.Write("selection",error);Note("转换未完成，请重试或使用备用窗口");}
  finally{busy=false;if(isolated)ExitThread();}
 }
 void ShowReadOnlyResult(string text){
  if(isolated){SelectionWorker.Emit("选区只读，已提供转换预览",text);return;}
  var form=new Form{Text="清简 · 转换结果（请手动复制）",Icon=LoadIcon(),Width=460,Height=170,StartPosition=FormStartPosition.CenterScreen};
  var box=new TextBox{Text=text,ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei",14)};
  var copy=new Button{Text="复制结果",Dock=DockStyle.Bottom,Height=36};copy.Click+=(s,e)=>{try{Clipboard.SetText(text);form.Close();}catch{form.Text="剪贴板忙，请重试复制";}};form.Controls.Add(box);form.Controls.Add(copy);form.Show();
 }
 protected override void ExitThreadCore(){
  if(closing)return;RuntimeLog.Write(isolated?"selection-worker-complete":"exit-request",null);closing=true;poll.Stop();Native.UnregisterHotKey(window.Handle,936);if(hook!=IntPtr.Zero)Native.UnhookWinEvent(hook);
  icon.Visible=false;icon.Dispose();window.DestroyHandle();
  // Closing this job terminates the owned Node/llama process tree, including on crash.
  if(job!=IntPtr.Zero)Native.CloseHandle(job);else if(backend!=null)try{backend.Kill();}catch{}
  if(exitEvent!=null)exitEvent.Dispose();dispatcher.Dispose();base.ExitThreadCore();
 }
}
static class Native {
 internal static uint Activity(){var info=new LastInputInfo();info.cbSize=(uint)Marshal.SizeOf(info);return GetLastInputInfo(ref info)?info.dwTime:0;}
 internal static bool WordDocumentFocused(){return WordFocusedHandle()!=IntPtr.Zero;}
 [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
 internal static IntPtr WordFocusedHandle(){uint owner;IntPtr foreground=GetForegroundWindow();uint thread=GetWindowThreadProcessId(foreground,out owner);var info=new GuiThreadInfo();info.size=(uint)Marshal.SizeOf(info);if(!GetGUIThreadInfo(thread,ref info))return IntPtr.Zero;IntPtr current=info.focus;
  for(int i=0;i<6&&current!=IntPtr.Zero;i++){uint process;GetWindowThreadProcessId(current,out process);if(process!=owner)break;var name=new System.Text.StringBuilder(128);GetClassName(current,name,name.Capacity);if(name.ToString()=="_WwG")return current;if(current==foreground)break;current=GetParent(current);}return IntPtr.Zero;
 }
 [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr ReadTextMessage(IntPtr h,uint msg,UIntPtr size,System.Text.StringBuilder text,uint flags,uint timeout,out UIntPtr result);
 [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr ReplaceTextMessage(IntPtr h,uint msg,UIntPtr undo,string text,uint flags,uint timeout,out UIntPtr result);
 internal static string ReadSelectedText(EditSelection selected){
  if(selected==null||selected.end>4096)return null;var current=ReadEditSelection(selected.handle,true);if(current==null||current.start!=selected.start||current.end!=selected.end)return null;
  var text=new System.Text.StringBuilder(selected.end+1);UIntPtr count;if(ReadTextMessage(selected.handle,0x0D,(UIntPtr)(selected.end+1),text,3,150,out count)==IntPtr.Zero||text.Length<selected.end)return null;return text.ToString().Substring(selected.start,selected.end-selected.start);
 }
 internal static bool ReplaceEditSelection(EditSelection selected,string expected,string text,bool requireForeground=true){
  var current=requireForeground?ForegroundEditSelection():ReadEditSelection(selected.handle,true);if(current==null||current.readOnly||current.handle!=selected.handle||current.start!=selected.start||current.end!=selected.end||ReadSelectedText(current)!=expected)return false;
  UIntPtr ignored;return ReplaceTextMessage(selected.handle,0xC2,(UIntPtr)1,text,3,200,out ignored)!=IntPtr.Zero;
 }
 internal static bool TypeUnicode(string text){
  if(!WordSelection.ValidResult(text))return false;var input=new Input[text.Length*2];for(int i=0;i<text.Length;i++){input[2*i].type=input[2*i+1].type=1;input[2*i].keyboard.scan=input[2*i+1].keyboard.scan=text[i];input[2*i].keyboard.flags=4;input[2*i+1].keyboard.flags=6;}return SendInput((uint)input.Length,input,Marshal.SizeOf(typeof(Input)))==input.Length;
 }
 [DllImport("user32.dll",SetLastError=true)]static extern bool OpenClipboard(IntPtr owner);
 [DllImport("user32.dll")]static extern bool CloseClipboard();
 [DllImport("user32.dll")]static extern IntPtr GetClipboardData(uint format);
 [DllImport("kernel32.dll")]static extern UIntPtr GlobalSize(IntPtr handle);
 [DllImport("kernel32.dll")]static extern IntPtr GlobalLock(IntPtr handle);
 [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr handle);
 [DllImport("ole32.dll",PreserveSig=true)]static extern int OleGetClipboard(out System.Runtime.InteropServices.ComTypes.IDataObject data);
 [DllImport("ole32.dll")]static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
 internal static byte[] RawClipboardFormat(int format){
  // Browsers expose virtual/delayed OLE formats which GetClipboardData does not
  // necessarily materialize. Ask the same OLE provider for opaque storage; never
  // deserialize arbitrary custom clipboard objects or drop formats silently.
  System.Runtime.InteropServices.ComTypes.IDataObject source=null;
  try{Marshal.ThrowExceptionForHR(OleGetClipboard(out source));return ReadRawClipboardFormat(source,format);}
  finally{if(source!=null&&Marshal.IsComObject(source))Marshal.ReleaseComObject(source);}
 }
 internal static byte[] ReadRawClipboardFormat(System.Runtime.InteropServices.ComTypes.IDataObject source,int format){
  var medium=new System.Runtime.InteropServices.ComTypes.STGMEDIUM();bool obtained=false;
  try{
   var request=new System.Runtime.InteropServices.ComTypes.FORMATETC{cfFormat=unchecked((short)format),dwAspect=System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,lindex=-1,ptd=IntPtr.Zero,tymed=System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL|System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM};
   source.GetData(ref request,out medium);obtained=true;
   if(medium.tymed==System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL){
    if(medium.unionmember==IntPtr.Zero)return new byte[0];ulong size=GlobalSize(medium.unionmember).ToUInt64();
    if(size>16*1024*1024)throw new IOException("单个剪贴板格式超过16MB");if(size==0)return new byte[0];
    IntPtr data=GlobalLock(medium.unionmember);if(data==IntPtr.Zero)throw new IOException("剪贴板内存无法读取");
    try{var bytes=new byte[(int)size];Marshal.Copy(data,bytes,0,bytes.Length);return bytes;}finally{GlobalUnlock(medium.unionmember);}
   }
   if(medium.tymed==System.Runtime.InteropServices.ComTypes.TYMED.TYMED_ISTREAM){
    object value=Marshal.GetObjectForIUnknown(medium.unionmember);IntPtr count=Marshal.AllocHGlobal(4);
    try{var stream=(System.Runtime.InteropServices.ComTypes.IStream)value;using(var output=new MemoryStream()){var buffer=new byte[65536];while(true){stream.Read(buffer,buffer.Length,count);int read=Marshal.ReadInt32(count);if(read<0||read>buffer.Length||output.Length+read>16*1024*1024)throw new IOException("剪贴板数据流过大或无效");if(read==0)return output.ToArray();output.Write(buffer,0,read);}}}
    finally{Marshal.FreeHGlobal(count);if(Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
   }
   throw new IOException("该剪贴板格式不能安全复制");
  }finally{if(obtained)ReleaseStgMedium(ref medium);}
 }
 internal class EditSelection{internal IntPtr handle;internal int start,end;internal bool readOnly;}
 [StructLayout(LayoutKind.Sequential)] struct Rect{public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo{public uint size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect rect;}
 internal static EditSelection ForegroundEditSelection(){
  uint process;uint thread=GetWindowThreadProcessId(GetForegroundWindow(),out process);var info=new GuiThreadInfo();info.size=(uint)Marshal.SizeOf(info);
  return GetGUIThreadInfo(thread,ref info)?ReadEditSelection(info.focus,true):null;
 }
 static EditSelection ReadEditSelection(IntPtr handle,bool allowReadOnly=false){
  if(handle==IntPtr.Zero||!IsWindowEnabled(handle))return null;
  var cls=new System.Text.StringBuilder(256);GetClassName(handle,cls,cls.Capacity);string name=cls.ToString().ToLowerInvariant();
  if(name!="edit"&&!name.StartsWith("richedit")&&!name.Contains(".edit."))return null;
  long style=GetWindowLongPtr(handle,-16).ToInt64();if((style&0x20)!=0||!allowReadOnly&&(style&0x800)!=0)return null;
  int start,end;UIntPtr result;
  if(SendSelectionMessage(handle,0xB0,out start,out end,3,100,out result)==IntPtr.Zero||start<0||end<=start||end-start>160)return null;
  return new EditSelection{handle=handle,start=start,end=end,readOnly=(style&0x800)!=0};
 }
 internal static void CheckEditCompatibility(){
  var results=new Dictionary<string,object>();using(var box=new TextBox()){
   box.Text="before nihao after";var h=box.Handle;box.Select(7,5);var selected=ReadEditSelection(h);results["nativeEditSelection"]=selected!=null&&selected.start==7&&selected.end==12;
   results["selectedTextReadWithoutClipboard"]=ReadSelectedText(selected)=="nihao";
   results["nativeReplacementWithoutClipboard"]=ReplaceEditSelection(selected,"nihao","你好",false)&&box.Text=="before 你好 after";
   box.Undo();results["nativeReplacementUndo"]=box.Text=="before nihao after";box.Select(7,5);
   box.ReadOnly=true;results["readOnlyRejected"]=ReadEditSelection(h)==null;results["readOnlyCanPreview"]=ReadEditSelection(h,true)!=null&&ReadEditSelection(h,true).readOnly;box.ReadOnly=false;box.UseSystemPasswordChar=true;results["passwordRejected"]=ReadEditSelection(box.Handle,true)==null;
  }File.WriteAllText(Path.Combine(Local.Root,"native-edit-tests.json"),Local.Json.Serialize(results));
 }
 [StructLayout(LayoutKind.Sequential)] internal struct LastInputInfo{internal uint cbSize,dwTime;}
 [StructLayout(LayoutKind.Sequential)] internal struct Keyboard{internal ushort vk,scan;internal uint flags,time;internal UIntPtr extra;}
 [StructLayout(LayoutKind.Explicit,Size=40)] internal struct Input{[FieldOffset(0)]internal uint type;[FieldOffset(8)]internal Keyboard keyboard;}
 internal static bool Chord(ushort key){var a=new Input[4];for(int i=0;i<4;i++){a[i].type=1;a[i].keyboard.vk=i==0||i==3?(ushort)0x11:key;a[i].keyboard.flags=i>=2?2u:0u;}return SendInput(4,a,40)==4;}
 internal class HotkeyWindow:NativeWindow{readonly Action run;internal HotkeyWindow(Action a){run=a;CreateHandle(new CreateParams());}protected override void WndProc(ref Message m){if(m.Msg==0x0312)run();base.WndProc(ref m);}}
 internal delegate void WinEventDelegate(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
 [StructLayout(LayoutKind.Sequential)] struct BasicLimit{public long a,b;public uint flags;public UIntPtr min,max;public uint limit;public IntPtr affinity;public uint priority,scheduling;}
 [StructLayout(LayoutKind.Sequential)] struct IO{public ulong a,b,c,d,e,f;}
 [StructLayout(LayoutKind.Sequential)] struct JobLimit{public BasicLimit basic;public IO io;public UIntPtr a,b,c,d;}
 internal static IntPtr CreateKillJob(){var job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Exception("后台进程保护不可用");var data=new JobLimit();data.basic.flags=0x2000;int size=Marshal.SizeOf(data);IntPtr p=Marshal.AllocHGlobal(size);try{Marshal.StructureToPtr(data,p,false);if(!SetInformationJobObject(job,9,p,(uint)size)){CloseHandle(job);throw new Exception("后台进程保护不可用");}}finally{Marshal.FreeHGlobal(p);}return job;}
 [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
 [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,System.Text.StringBuilder name,int size);
 [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")]static extern IntPtr SendSelectionMessage(IntPtr h,uint msg,out int start,out int end,uint flags,uint timeout,out UIntPtr result);
 [DllImport("user32.dll")]internal static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]internal static extern bool GetLastInputInfo(ref LastInputInfo info);
 [DllImport("user32.dll")]internal static extern uint GetClipboardSequenceNumber();
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint n,Input[] a,int size);
 [DllImport("user32.dll")]internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventDelegate callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")]internal static extern bool UnhookWinEvent(IntPtr h);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr a,string name);
 [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr h,int type,IntPtr info,uint size);
 [DllImport("kernel32.dll")]internal static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")]internal static extern bool CloseHandle(IntPtr h);
}
