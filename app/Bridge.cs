using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
class Bridge {
 static byte[] Read(Stream s,int size){var b=new byte[size];int n=0,r;while(n<size){r=s.Read(b,n,size-n);if(r==0)return null;n+=r;}return b;}
 static void Main(string[] args){
  try {
   string origin="chrome-extension://"+Local.Config["extensionId"]+"/";
   if(args.Length==0||args[0]!=origin)return;
   if(String.IsNullOrEmpty(Environment.GetEnvironmentVariable("QJ_TEST_PIPE")))Local.PruneOldExecutables();
   var input=Console.OpenStandardInput();var output=Console.OpenStandardOutput();
   while(true){var header=Read(input,4);if(header==null)return;int length=BitConverter.ToInt32(header,0);if(length<2||length>16384)return;
    var bytes=Read(input,length);if(bytes==null)return;string result;
    object requestId=null;
    try{var m=Local.Json.Deserialize<Dictionary<string,object>>(new UTF8Encoding(false,true).GetString(bytes));if(m==null)throw new IOException();m.TryGetValue("requestId",out requestId);if(requestId!=null&&!(requestId is int))throw new IOException();object action;if(m.TryGetValue("action",out action)&&!(action is string&&((string)action=="status"||(string)action=="learn"||(string)action=="clearLearning")))throw new IOException();foreach(string field in new[]{"text","before","after"}){object value;if(m.TryGetValue(field,out value)&&(!(value is string)||((string)value).Length>(field=="text"?160:48)))throw new IOException();}result=Local.Rpc(m,12000);}
    catch(Exception){result=Local.Json.Serialize(new{error="本地请求未完成，请检查清简状态",unavailable=true});}
    var response=Local.Json.Deserialize<Dictionary<string,object>>(result);response["requestId"]=requestId;
    bytes=Encoding.UTF8.GetBytes(Local.Json.Serialize(response));if(bytes.Length>65536)return;output.Write(BitConverter.GetBytes(bytes.Length),0,4);output.Write(bytes,0,bytes.Length);output.Flush();
   }
  }catch{/* stdout is strictly reserved for native-messaging frames. */}
 }
}
