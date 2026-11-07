using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

static class WordSelection {
 [DllImport("oleacc.dll",PreserveSig=true)]static extern int AccessibleObjectFromWindow(IntPtr window,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object nativeObject);
 internal static async Task<bool> TryConvert(Func<string,Task<string>> query,Action<string> status,Action<string> preview){
  // Bind the focused document, rather than the first Word registered in the ROT.
  // This path never uses UI Automation, clipboard or simulated keyboard input.
  IntPtr documentHandle=Native.WordFocusedHandle(),foreground=Native.GetForegroundWindow();
  if(documentHandle==IntPtr.Zero){status("焦点不在 Word 正文，请先在正文中选中拼音");return true;}
  object window=null,application=null,selection=null,document=null,range=null;bool attemptedWrite=false,bound=false;
  try{
   var iid=new Guid("00020400-0000-0000-C000-000000000046");
   int hr=AccessibleObjectFromWindow(documentHandle,0xfffffff0u,ref iid,out window);
   if(hr==0&&window!=null){application=((dynamic)window).Application;selection=((dynamic)window).Selection;document=((dynamic)window).Document;}
   else{
    application=Marshal.GetActiveObject("Word.Application");dynamic registered=application;
    if(new IntPtr(Convert.ToInt64(registered.ActiveWindow.Hwnd))!=foreground){status("Word 当前窗口与文档接口不一致，已取消");return true;}
    selection=registered.Selection;document=registered.ActiveDocument;
   }
   bound=true;dynamic app=application;range=((dynamic)selection).Range.Duplicate;
   dynamic selected=selection,doc=document,target=range;string original=Convert.ToString(target.Text);int start=target.Start,end=target.End,story=target.StoryType;
   if(String.IsNullOrEmpty(original)||start==end){status("Word 没有文字选区，请拖选已输入的拼音");return true;}
   char[] padding={' ','\t','\r','\n','\a','\u00a0'};string input=original.Trim(padding);int leading=original.Length-original.TrimStart(padding).Length;
   if(!Valid(input.Replace('\u00a0',' '))){status("Word 选区不是单段拼音，原文保留");return true;}
   bool readOnly=doc.ReadOnly||Convert.ToInt32(doc.ProtectionType)!=-1;uint activity=Native.Activity();
   status("正在通过 Word 正文接口转换选区");string output=await query(input.Replace('\u00a0',' ').ToLowerInvariant());
   if(!ValidResult(output)){status("未获得有效候选，原文保留");return true;}
   if(activity==0||Native.Activity()!=activity||Native.GetForegroundWindow()!=foreground||Native.WordFocusedHandle()!=documentHandle||Convert.ToInt32(selected.Start)!=start||Convert.ToInt32(selected.End)!=end||Convert.ToInt32(selected.StoryType)!=story||Convert.ToString(selected.Range.Text)!=original||!SameObject(document,(object)app.ActiveDocument)){status("Word 选区或输入已变化，已取消替换");return true;}
   if(readOnly){preview(output);return true;}
   // Leave paragraph marks, table-cell terminators and outside whitespace intact.
   target.SetRange(start+leading,start+leading+input.Length);attemptedWrite=true;target.Text=output;
   status("已通过 Word 正文接口完成转换，剪贴板未改动");return true;
  }catch(Exception error){RuntimeLog.Write("word-object-model",error);status(attemptedWrite?"Word 未确认替换结果，请检查原文；不会重复执行":"Word 接口暂不可用（0x"+Marshal.GetHRForException(error).ToString("X8")+"），原文保留");return attemptedWrite||bound;}
  finally{foreach(object value in new[]{range,selection,document,application,window})try{if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}catch{}}
 }
 static bool SameObject(object a,object b){IntPtr x=IntPtr.Zero,y=IntPtr.Zero;try{x=Marshal.GetIUnknownForObject(a);y=Marshal.GetIUnknownForObject(b);return x==y;}finally{if(x!=IntPtr.Zero)Marshal.Release(x);if(y!=IntPtr.Zero)Marshal.Release(y);}}
 internal static bool Valid(string text){return !String.IsNullOrWhiteSpace(text)&&text.Length<=160&&System.Text.RegularExpressions.Regex.IsMatch(text,@"^[a-züv' \t]+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);}
 internal static bool ValidResult(string text){return text!=null&&System.Text.RegularExpressions.Regex.IsMatch(text,@"^[\u3400-\u9fff]{1,80}$");}
}
