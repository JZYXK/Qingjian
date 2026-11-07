using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

class FallbackWindow:Form {
 readonly TextBox source,result;readonly Label status;readonly Button convert,copy;readonly Func<string,Task<string>> query;
 internal FallbackWindow(Func<string,Task<string>> converter,Icon icon=null){
  query=converter;Text="清简 · 备用转换";if(icon!=null)Icon=icon;
  Font=new Font("Microsoft YaHei UI",10);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
  ClientSize=new Size(600,440);MinimumSize=new Size(500,400);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(245,242,234);
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(20),BackColor=BackColor};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  for(int i=0;i<8;i++)layout.RowStyles.Add(new RowStyle(i==3||i==5?SizeType.Percent:SizeType.AutoSize,i==3||i==5?50:0));
  var title=new Label{Text="备用转换",AutoSize=true,Font=new Font(Font.FontFamily,17,FontStyle.Bold),ForeColor=Color.FromArgb(23,32,57),Margin=new Padding(0,0,0,6)};
  var tip=new Label{Text="将拼音粘贴到下面，转换后复制回原软件。Ctrl+Enter 可快速转换。",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12),ForeColor=Color.FromArgb(90,98,112)};
  var sourceLabel=new Label{Text="拼音",AutoSize=true,Margin=new Padding(0,0,0,5)};
  source=new TextBox{Name="qjSource",Multiline=true,MaxLength=160,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Margin=new Padding(0,0,0,12),BorderStyle=BorderStyle.FixedSingle,AcceptsReturn=true};
  var resultLabel=new Label{Text="转换结果",AutoSize=true,Margin=new Padding(0,0,0,5)};
  result=new TextBox{Name="qjResult",Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,Margin=new Padding(0,0,0,10),BorderStyle=BorderStyle.FixedSingle};
  status=new Label{Name="qjStatus",Text="仅处理你主动粘贴的内容。",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10),ForeColor=Color.FromArgb(90,98,112)};
  var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,Margin=new Padding(0)};
  copy=new Button{Name="qjCopy",Text="复制结果",AutoSize=true,MinimumSize=new Size(110,38),Padding=new Padding(12,5,12,5),Margin=new Padding(8,0,0,0),Enabled=false};
  convert=new Button{Name="qjConvert",Text="转换",AutoSize=true,MinimumSize=new Size(110,38),Padding=new Padding(12,5,12,5),Margin=new Padding(0),BackColor=Color.FromArgb(49,93,141),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};convert.FlatAppearance.BorderSize=0;
  buttons.Controls.Add(copy);buttons.Controls.Add(convert);
  layout.Controls.Add(title,0,0);layout.Controls.Add(tip,0,1);layout.Controls.Add(sourceLabel,0,2);layout.Controls.Add(source,0,3);layout.Controls.Add(resultLabel,0,4);layout.Controls.Add(result,0,5);layout.Controls.Add(status,0,6);layout.Controls.Add(buttons,0,7);Controls.Add(layout);
  source.TextChanged+=(s,e)=>copy.Enabled=false;convert.Click+=async(s,e)=>await Run();
  source.KeyDown+=async(s,e)=>{if(e.Control&&e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;if(convert.Enabled)await Run();}};
  copy.Click+=(s,e)=>{try{Clipboard.SetText(result.Text);status.Text="已复制，请回原软件 Ctrl+V。";}catch{status.Text="剪贴板忙，请再点一次复制。";}};
  Shown+=(s,e)=>source.Focus();
 }
 async Task Run(){
  string input=source.Text;if(!System.Text.RegularExpressions.Regex.IsMatch(input,@"^[a-züv' \r\n\t]{1,160}$")){status.Text="请输入不超过160字符的小写拼音。";return;}convert.Enabled=false;copy.Enabled=false;status.Text="正在转换…";
  try{string output=await query(input);if(IsDisposed||source.Text!=input)return;if(output!=null&&System.Text.RegularExpressions.Regex.IsMatch(output,@"^[\u3400-\u9fff]{1,80}$")){result.Text=output;copy.Enabled=true;status.Text="转换完成，复制后回原软件粘贴。";}else status.Text="没有识别出完整拼音，原内容已保留。";}
  catch(Exception error){RuntimeLog.Write("fallback",error);if(!IsDisposed)status.Text="后台正在准备或恢复，请稍后重试。";}
  finally{if(!IsDisposed)convert.Enabled=true;}
 }
 static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
 internal static void CheckLayout(){
  var checks=new List<object>();bool all=true;
  foreach(float factor in new[]{1F,1.5F,2F})using(var form=new FallbackWindow(s=>Task.FromResult("你好"))){
   var fonts=new Dictionary<Control,Font>();foreach(Control child in Descendants(form))fonts[child]=child.Font;
   form.Font=new Font(form.Font.FontFamily,10*factor);foreach(var pair in fonts)pair.Key.Font=new Font(pair.Value.FontFamily,pair.Value.SizeInPoints*factor,pair.Value.Style);
   form.ClientSize=new Size((int)(600*factor),(int)(440*factor));form.source.Text="nihao";form.result.Text="你好";form.copy.Enabled=true;form.status.Text="已复制，请回原软件 Ctrl+V。";form.Show();Application.DoEvents();form.PerformLayout();
   bool ok=true;foreach(var control in new Control[]{form.source,form.result,form.copy,form.convert}){var bounds=form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));ok&=bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=form.ClientSize.Width&&bounds.Bottom<=form.ClientSize.Height&&control.Height>=control.Font.Height+8;}
   ok&=form.copy.ClientSize.Width>=TextRenderer.MeasureText(form.copy.Text,form.copy.Font).Width+8;ok&=form.convert.ClientSize.Width>=TextRenderer.MeasureText(form.convert.Text,form.convert.Font).Width+8;
   using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(Local.Root,"fallback-layout-"+(int)(factor*100)+".png"));}checks.Add(new{scale=factor,passed=ok,width=form.Width,height=form.Height});all&=ok;form.Close();
  }
  File.WriteAllText(Path.Combine(Local.Root,"fallback-layout-tests.json"),Local.Json.Serialize(new{passed=all,scope="Real WinForms controls with scaled fonts/client sizes (100/150/200 percent); not a multi-monitor DPI hardware test",checks=checks}));Environment.ExitCode=all?0:1;
 }
}
