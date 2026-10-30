using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO.Ports;
using System.Diagnostics;
using System.Web.Script.Serialization;

class MeterData {
 public Dictionary<int,Dictionary<string,string>> Meta=new Dictionary<int,Dictionary<string,string>>();
 public Dictionary<int,double> Values=new Dictionary<int,double>(), Frequencies=new Dictionary<int,double>();
 public DateTime Last=DateTime.MinValue;
 public Dictionary<int,DateTime> Times=new Dictionary<int,DateTime>();
 public int Packets=0, MeterPackets=0;
 public void Line(string line) {
  Match removed=Regex.Match(line,@"\|meter (\d+) removed");if(removed.Success){int id=int.Parse(removed.Groups[1].Value);Meta.Remove(id);Values.Remove(id);Times.Remove(id);return;}
  foreach(Match m in Regex.Matches(line,@"(\d+)\.(src|num|nam|unit)=([^#|\r\n]+)")) {
   int id=int.Parse(m.Groups[1].Value); if(!Meta.ContainsKey(id))Meta[id]=new Dictionary<string,string>();
   Meta[id][m.Groups[2].Value]=m.Groups[3].Value.Trim().Trim('"');
  }
  Match s=Regex.Match(line,@"\|slice (\d+) (.*)");
  if(s.Success) {int id=int.Parse(s.Groups[1].Value); if(s.Groups[2].Value.Contains("removed")){Frequencies.Remove(id);return;}
   Match f=Regex.Match(s.Groups[2].Value,@"(?:^| )RF_frequency=([0-9.]+)");
   if(f.Success)Frequencies[id]=double.Parse(f.Groups[1].Value,CultureInfo.InvariantCulture);
  }
 }
 public static int U16(byte[] b,int p){return (b[p]<<8)|b[p+1];}
 public void Packet(byte[] b) {
  Packets++;
  if(b.Length<16)return; int type=b[0]>>4; if(type!=3)return;
  int end=U16(b,2)*4; if(end>b.Length||end<16||(b[0]&8)==0)return;
  if(U16(b,14)!=0x8002||b[9]!=0||b[10]!=0x1C||b[11]!=0x2D)return;
  int p=16; if((b[1]&0xC0)!=0)p+=4;if((b[1]&0x30)!=0)p+=8;if((b[0]&4)!=0)end-=4;
  if(end<p||(end-p)%4!=0)return;MeterPackets++;
  for(;p+3<end;p+=4){int id=U16(b,p);if(!Meta.ContainsKey(id))continue;
   var m=Meta[id];string u; if(!m.TryGetValue("unit",out u)||!u.Equals("dBm",StringComparison.OrdinalIgnoreCase))continue;
   double value=unchecked((short)U16(b,p+2))/128.0;if(value < -160||value>30)continue;
   Values[id]=value;Last=DateTime.UtcNow;Times[id]=Last;
  }
 }
 public bool Reading(int slice,out double value){foreach(var entry in Meta){var m=entry.Value;
  if(m.ContainsKey("src")&&m["src"]=="SLC"&&m.ContainsKey("nam")&&m["nam"]=="LEVEL"&&m.ContainsKey("num")&&m["num"]==slice.ToString()&&Times.ContainsKey(entry.Key)&&DateTime.UtcNow-Times[entry.Key]<TimeSpan.FromSeconds(2)&&Values.TryGetValue(entry.Key,out value))return true;
 }value=-127;return false;}
}

class PeakMemory {
 public double Value=-127;public bool Hold=false;DateTime until=DateTime.MinValue;
 public void Reset(){Value=-127;until=DateTime.MinValue;}
 public void Update(double value,DateTime now,double seconds){if(value>=Value){Value=value;until=now.AddSeconds(2);}else if(!Hold&&now>until)Value=Math.Max(value,Value-18*seconds);}
}
class LogbookButton:Button {
 bool hover,pressed;
 public LogbookButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnPaintBackground(PaintEventArgs e){}
 protected override void OnPaint(PaintEventArgs e){if(Width<4||Height<4)return;using(var buffer=new Bitmap(Width*3,Height*3,System.Drawing.Imaging.PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(buffer)){
  g.ScaleTransform(3,3);g.SmoothingMode=SmoothingMode.HighQuality;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
  if(Parent is FlexMeter){using(var background=FlexMeter.FrameGradient(new Rectangle(-Left,-Top,Parent.ClientSize.Width,Parent.ClientSize.Height)))g.FillRectangle(background,ClientRectangle);}else g.Clear(Parent==null?Color.FromArgb(7,31,52):Parent.BackColor);
  float scale=Font.Size/9f;float stroke=Math.Max(1,scale*.7f),inset=stroke/2+1;float x=inset,y=inset,w=Width-inset*2,h=Height-inset*2;float d=Math.Min(24*scale,Math.Min(w,h));
  if(w<=0||h<=0)return;using(var path=new GraphicsPath()){path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();
   bool primary=BackColor.B>150;Color a=primary?Color.FromArgb(0,130,255):Color.FromArgb(11,42,72),b=primary?Color.FromArgb(0,74,229):Color.FromArgb(7,27,49);
   if(hover&&!primary)a=b=Color.FromArgb(13,53,90);if(pressed){a=Color.FromArgb(0,74,170);b=Color.FromArgb(4,40,80);}
   using(var brush=new LinearGradientBrush(ClientRectangle,a,b,135f))g.FillPath(brush,path);
   using(var pen=new Pen(hover||primary||(Focused&&ShowFocusCues)?Color.FromArgb(0,143,255):Color.FromArgb(32,71,103),stroke))g.DrawPath(pen,path);
  }
  using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})using(var text=new SolidBrush(Enabled?Color.FromArgb(241,245,255):Color.FromArgb(110,135,155)))g.DrawString(Text,Font,text,new RectangleF(0,0,Width,Height),format);
  e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;e.Graphics.DrawImage(buffer,ClientRectangle,0,0,buffer.Width,buffer.Height,GraphicsUnit.Pixel);
 }}
}
class InfoButton:Button {
 public InfoButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;AccessibleName="Sobre o Flex Meter";Text="i";TabStop=true;}
 protected override void OnPaintBackground(PaintEventArgs e){}
 protected override void OnPaint(PaintEventArgs e){if(Width<4||Height<4)return;using(var bitmap=new Bitmap(Width*3,Height*3))using(var g=Graphics.FromImage(bitmap)){g.ScaleTransform(3,3);g.SmoothingMode=SmoothingMode.HighQuality;
  using(var bg=FlexMeter.FrameGradient(new Rectangle(-Left,-Top,Parent.ClientSize.Width,Parent.ClientSize.Height)))g.FillRectangle(bg,ClientRectangle);
  float size=Math.Min(Width,Height)-3;float x=(Width-size)/2,y=(Height-size)/2;
  using(var pen=new Pen(Focused?Color.WhiteSmoke:Color.FromArgb(73,170,255),1.2f))g.DrawEllipse(pen,x,y,size,size);
  using(var font=new Font("Arial",size*.60f,FontStyle.Regular,GraphicsUnit.Pixel))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.DrawString("i",font,Brushes.WhiteSmoke,new RectangleF(x,y-1,size,size),format);}
  e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.DrawImage(bitmap,ClientRectangle,0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel);
 }}
}
class MovableDialog:Form {
 public MovableDialog(){TopMost=true;}
 [DllImport("user32.dll")]static extern bool ReleaseCapture();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 void Drag(MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;ReleaseCapture();SendMessage(Handle,0x00A1,new IntPtr(2),IntPtr.Zero);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Drag(e);}
 protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);if(e.Control is Label){e.Control.Cursor=Cursors.SizeAll;e.Control.MouseDown+=delegate(object sender,MouseEventArgs mouse){Drag(mouse);};}}
}
partial class FlexMeter:Form {
 const string ReleasesUrl="https://github.com/CT7ASY/FlexMeter/releases/latest";
 bool updateCheckRunning=false;
 public static Version ParseReleaseVersion(string tag){var m=Regex.Match(tag??"",@"^v?(\d+\.\d+(?:\.\d+){0,2})$");Version v;if(!m.Success||!Version.TryParse(m.Groups[1].Value,out v))return null;return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
 public static Version PublishedVersion(string json){var data=new JavaScriptSerializer{MaxJsonLength=1048576}.Deserialize<Dictionary<string,object>>(json);object value;if(data==null||!data.TryGetValue("tag_name",out value))return null;object draft,pre;if((data.TryGetValue("draft",out draft)&&draft is bool&&(bool)draft)||(data.TryGetValue("prerelease",out pre)&&pre is bool&&(bool)pre))return null;return ParseReleaseVersion(value as string);}
 const string AppVersion="0.69";
 string callsign="";bool callsignConfigured=false;
 public static string NormalizeCallsign(string value){return value.Trim().ToUpperInvariant();}
 public static bool ValidCallsign(string value){return value.Length==0||Regex.IsMatch(value,@"^[A-Z0-9/\-]{1,20}$");}
 public static Point FreeDialogLocation(Size size,Rectangle[] areas,Rectangle[] occupied,Rectangle owner){Point best=owner.Location;long bestOverlap=long.MaxValue;double bestDistance=double.MaxValue;
  foreach(Rectangle area in areas){var xs=new List<int>{area.Left,area.Right-size.Width,owner.Right+16,owner.Left-size.Width-16};var ys=new List<int>{area.Top,area.Bottom-size.Height,owner.Top};
   foreach(Rectangle r in occupied){xs.Add(r.Left);xs.Add(r.Right+16);xs.Add(r.Left-size.Width-16);ys.Add(r.Top);ys.Add(r.Bottom+16);ys.Add(r.Top-size.Height-16);}
   foreach(int x0 in xs)foreach(int y0 in ys){int x=Math.Max(area.Left,Math.Min(area.Right-size.Width,x0)),y=Math.Max(area.Top,Math.Min(area.Bottom-size.Height,y0));var candidate=new Rectangle(new Point(x,y),size);long overlap=0;
    foreach(Rectangle r in occupied){var padded=r;padded.Inflate(8,8);Rectangle intersection=Rectangle.Intersect(candidate,padded);overlap+=(long)intersection.Width*intersection.Height;}
    double dx=x-(owner.Right+16),dy=y-owner.Top;double distance=dx*dx+dy*dy;
    if(overlap<bestOverlap||(overlap==bestOverlap&&distance<bestDistance)){best=candidate.Location;bestOverlap=overlap;bestDistance=distance;}
   }
  }return best;
 }
 static DialogResult ShowWithoutOverlap(Form dialog,Form owner){var occupied=new List<Rectangle>();foreach(Form form in Application.OpenForms)if(form!=dialog&&form.Visible&&form.WindowState!=FormWindowState.Minimized)occupied.Add(form.Bounds);
  var screens=new List<Rectangle>{Screen.FromControl(owner).WorkingArea};foreach(Screen screen in Screen.AllScreens)if(!screens.Contains(screen.WorkingArea))screens.Add(screen.WorkingArea);
  dialog.StartPosition=FormStartPosition.Manual;dialog.Location=FreeDialogLocation(dialog.Size,screens.ToArray(),occupied.ToArray(),owner.Bounds);return dialog.ShowDialog(owner);
 }
 void ShowIndependent(Form dialog,Form near){var occupied=new List<Rectangle>();foreach(Form form in Application.OpenForms)if(form!=dialog&&form.Visible&&form.WindowState!=FormWindowState.Minimized)occupied.Add(form.Bounds);var screens=new List<Rectangle>{Screen.FromControl(near).WorkingArea};foreach(Screen screen in Screen.AllScreens)if(!screens.Contains(screen.WorkingArea))screens.Add(screen.WorkingArea);dialog.StartPosition=FormStartPosition.Manual;dialog.Location=FreeDialogLocation(dialog.Size,screens.ToArray(),occupied.ToArray(),near.Bounds);dialog.ShowInTaskbar=false;dialog.Show();ArrangeIndependentWindows();}
 void StyleConfiguration(Form dialog,Button save,Label note){
  dialog.BackColor=Color.FromArgb(11,34,56);Color field=Color.FromArgb(18,47,73),edge=Color.FromArgb(79,119,151),focus=Color.FromArgb(0,164,255);
  var controls=new Control[dialog.Controls.Count];dialog.Controls.CopyTo(controls,0);
  foreach(Control control in controls){control.ForeColor=Color.FromArgb(241,245,255);
   if(control is Label){control.BackColor=dialog.BackColor;continue;}
   var box=control as ComboBox;var input=control as TextBox;
   if(box==null&&input==null)continue;
   control.BackColor=field;
   control.Enter+=delegate{dialog.Invalidate();};control.Leave+=delegate{dialog.Invalidate();};
   if(input!=null)input.BorderStyle=BorderStyle.FixedSingle;
   if(box!=null){box.FlatStyle=FlatStyle.Flat;box.DrawMode=DrawMode.OwnerDrawFixed;box.ItemHeight=24;
    box.DrawItem+=delegate(object sender,DrawItemEventArgs e){if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;
     using(var fill=new SolidBrush(selected?Color.FromArgb(0,88,176):field))e.Graphics.FillRectangle(fill,e.Bounds);
     string text=box.GetItemText(box.Items[e.Index]);var r=e.Bounds;r.X+=7;r.Width-=9;
     TextRenderer.DrawText(e.Graphics,text,box.Font,r,Color.FromArgb(241,245,255),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    };
   }
  }
  save.BackColor=Color.FromArgb(0,91,211);note.ForeColor=Color.FromArgb(193,213,233);
  dialog.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(edge,1))e.Graphics.DrawRectangle(pen,0,0,dialog.ClientSize.Width-1,dialog.ClientSize.Height-1);foreach(Control c in controls)if(c.Visible&&(c is TextBox||c is ComboBox)){var r=c.Bounds;r.Inflate(1,1);using(var pen=new Pen(c.Focused?focus:edge,1))e.Graphics.DrawRectangle(pen,r);}};
 }
 Form configurationWindow,aboutWindow;
 void About(Form owner=null){if(aboutWindow!=null&&!aboutWindow.IsDisposed){aboutWindow.Activate();ArrangeIndependentWindows();return;}var dialog=new MovableDialog();aboutWindow=dialog;dialog.FormClosed+=delegate{aboutWindow=null;if(!closing)ArrangeIndependentWindows();};dialog.Text=T("Sobre o Flex Meter");dialog.StartPosition=FormStartPosition.CenterParent;dialog.FormBorderStyle=FormBorderStyle.None;dialog.MaximizeBox=false;dialog.MinimizeBox=false;dialog.ClientSize=new Size(440,376);dialog.BackColor=Color.FromArgb(7,31,52);dialog.ForeColor=Color.FromArgb(241,245,255);dialog.Font=new Font("Arial",10);
  var title=new Label{Text="FLEX METER",Font=new Font("Arial",17,FontStyle.Regular),TextAlign=ContentAlignment.MiddleCenter};title.SetBounds(24,20,390,38);
  var version=new Label{Tag="@version",Text=T("Versão ")+AppVersion,ForeColor=Color.FromArgb(73,170,255),TextAlign=ContentAlignment.MiddleCenter};version.SetBounds(24,62,390,24);
  var text=new Label{Tag="@about",Text=T("Medidor de sinal para FlexRadio, com leitura contínua e retenção de pico.")+"\n\n"+T("Ligação por rede local ou SmartSDR CAT.")+"\n\n"+T("Desenvolvido por Eduardo Arraia - CT7ASY")+"\n\n"+T("Software de distribuição gratuita."),TextAlign=ContentAlignment.MiddleCenter};text.SetBounds(24,100,392,164);
  var update=new LogbookButton{Tag="Verificar atualizações",Text=T("Verificar atualizações"),BackColor=Color.FromArgb(3,22,40)};update.SetBounds(80,280,280,30);update.Click+=async delegate{update.Enabled=false;try{await CheckForUpdates(true,dialog);}finally{if(!update.IsDisposed)update.Enabled=true;}};
  var close=new LogbookButton{Tag="Fechar",Text=T("Fechar"),DialogResult=DialogResult.OK,BackColor=Color.FromArgb(3,22,40)};close.SetBounds(167,326,106,30);dialog.Controls.AddRange(new Control[]{title,version,text,update,close});dialog.AcceptButton=close;dialog.CancelButton=close;close.Click+=delegate{dialog.Close();};ShowIndependent(dialog,owner??this);
 }
 public static LinearGradientBrush FrameGradient(Rectangle bounds){var brush=new LinearGradientBrush(bounds,Color.FromArgb(34,74,106),Color.FromArgb(4,19,33),90f);brush.InterpolationColors=new ColorBlend{Colors=new[]{Color.FromArgb(34,74,106),Color.FromArgb(13,40,62),Color.FromArgb(4,19,33)},Positions=new[]{0f,.45f,1f}};return brush;}
 readonly System.Windows.Forms.Timer reconnectTimer=new System.Windows.Forms.Timer();bool closing=false;
 void AutoConnect(){if(closing||simulated||cancel!=null)return;if(catMode){if(!string.IsNullOrWhiteSpace(comPort))StartCat();}else {IPAddress address;if(IPAddress.TryParse(ip.Text.Trim(),out address))Start(ip.Text.Trim());}}
 static readonly Brush SignalRedBrush=new SolidBrush(Color.FromArgb(239,43,53));
 bool catMode=false;string comPort="";int baud=9600;SerialPort serial;double catValue=-127;DateTime catTime=DateTime.MinValue;
 static string SettingsPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CT7ASY","FlexMeter","settings.ini");}}
 void LoadWindow(){try{string path=Path.Combine(Path.GetDirectoryName(SettingsPath),"window.ini");if(!File.Exists(path))return;string[] parts=File.ReadAllText(path).Split(',');int x,y,w,h;if(parts.Length!=4||!int.TryParse(parts[0],out x)||!int.TryParse(parts[1],out y)||!int.TryParse(parts[2],out w)||!int.TryParse(parts[3],out h)||w<MinimumSize.Width||h<MinimumSize.Height||w>10000||h>10000)return;
  h=(int)Math.Round(w*340.0/500);var bounds=new Rectangle(x,y,w,h);foreach(var screen in Screen.AllScreens){Rectangle visible=Rectangle.Intersect(bounds,screen.WorkingArea);if(visible.Width>=100&&visible.Height>=100){StartPosition=FormStartPosition.Manual;Bounds=bounds;return;}}}catch(IOException){} }
 void SaveWindow(){if(simulated)return;try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));Rectangle r=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;File.WriteAllText(Path.Combine(Path.GetDirectoryName(SettingsPath),"window.ini"),r.X+","+r.Y+","+r.Width+","+r.Height);}catch(IOException){}catch(UnauthorizedAccessException){} }
 void LoadSettings(){try{if(!File.Exists(SettingsPath))return;foreach(string line in File.ReadAllLines(SettingsPath)){int p=line.IndexOf('=');if(p<0)continue;string key=line.Substring(0,p),value=line.Substring(p+1);int n;
  if(key=="language"&&(value=="pt"||value=="es"||value=="en"))language=value;
  if(key=="callsign"&&ValidCallsign(NormalizeCallsign(value))){callsign=NormalizeCallsign(value);callsignConfigured=true;}
  if(key=="ip")ip.Text=value;if(key=="slice"&&int.TryParse(value,out n)&&n>=0&&n<slice.Items.Count)slice.SelectedIndex=n;if(key=="cat")catMode=value=="1";if(key=="com")comPort=value;if(key=="baud"&&int.TryParse(value,out n)&&n>0)baud=n;}}catch(IOException){} }
 void Configure(){if(configurationWindow!=null&&!configurationWindow.IsDisposed){configurationWindow.Activate();ArrangeIndependentWindows();return;}var dialog=new MovableDialog();configurationWindow=dialog;dialog.FormClosed+=delegate{configurationWindow=null;if(!closing)ArrangeIndependentWindows();};dialog.Text=T("Configuração")+" • Flex Meter";dialog.StartPosition=FormStartPosition.CenterParent;dialog.ClientSize=new Size(420,405);dialog.FormBorderStyle=FormBorderStyle.None;dialog.MaximizeBox=false;dialog.MinimizeBox=false;dialog.BackColor=Color.FromArgb(7,31,52);dialog.ForeColor=Color.WhiteSmoke;dialog.Font=new Font("Arial",10);
  var mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};mode.Items.AddRange(new object[]{"Direto ao Flex por IP","SmartSDR CAT por porta série"});mode.SelectedIndex=catMode?1:0;mode.SetBounds(170,20,298,28);
  var host=new TextBox{Text=ip.Text};host.SetBounds(170,68,298,28);var sl=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};sl.Items.AddRange(new object[]{"Slice A (0)","Slice B (1)"});sl.SelectedIndex=slice.SelectedIndex;sl.SetBounds(170,110,298,28);
  var com=new ComboBox{Text=comPort};com.Items.AddRange(SerialPort.GetPortNames());com.SetBounds(170,158,200,28);
  var refresh=new LogbookButton{Text="Atualizar"};refresh.SetBounds(380,158,100,28);refresh.Click+=delegate{string old=com.Text;com.Items.Clear();com.Items.AddRange(SerialPort.GetPortNames());com.Text=old;};
  var speed=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};speed.Items.AddRange(new object[]{"4800","9600","19200","38400","57600","115200"});speed.SelectedItem=baud.ToString();if(speed.SelectedIndex<0)speed.SelectedIndex=1;speed.SetBounds(170,200,200,28);
  string[] labels={"Tipo de ligação","Endereço IP do Flex","Slice","Porta COM","Velocidade"};var fieldLabels=new Label[5];for(int i=0;i<labels.Length;i++){var l=new Label{Text=labels[i],AutoSize=false};fieldLabels[i]=l;dialog.Controls.Add(l);}
  var create=new LogbookButton{Text="Criar porta no SmartSDR CAT…"};create.SetBounds(20,248,460,32);create.Click+=delegate{try{string root=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);string[] installs=Directory.GetDirectories(root,"SmartSDR*");string exe=null;foreach(string folder in installs){string candidate=Path.Combine(folder,"CAT","Cat.exe");if(File.Exists(candidate))exe=candidate;}if(exe!=null)Process.Start(exe);else MessageBox.Show(dialog,T("Abre o SmartSDR CAT pelo menu Iniciar."));}catch(Exception ex){MessageBox.Show(dialog,ex.Message);}MessageBox.Show(dialog,T("No SmartSDR CAT: Add → Serial → CAT. Cria uma porta dedicada, escolhe a slice e guarda. Depois carrega em Atualizar e escolhe aqui a COM apresentada pelo CAT.\n\nA slice da ligação CAT é definida no SmartSDR CAT."),T("Criar porta série virtual"));};
  var note=new Label{TextAlign=ContentAlignment.MiddleCenter,Text="CAT: usa uma COM dedicada do SmartSDR CAT (sem PTT).\nA slice CAT é escolhida no SmartSDR CAT; por IP é escolhida acima."};note.SetBounds(20,292,470,48);
  var callLabel=new Label{Text="Indicativo (opcional)"};
  var callInput=new TextBox{Text=callsign,MaxLength=20,CharacterCasing=CharacterCasing.Upper};
  var languageLabel=new Label{Visible=false};var flags=new LanguageFlags{AccessibleName=T("Idioma")};
  var languagePicker=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};languagePicker.Items.AddRange(new object[]{"Português","Español","English"});languagePicker.SelectedIndex=language=="es"?1:language=="en"?2:0;
  var about=new LogbookButton{Text="Sobre"};about.Click+=delegate{About(dialog);};
  var save=new LogbookButton{Text="Guardar"};save.SetBounds(260,355,105,32);var close=new LogbookButton{Text="Cancelar",DialogResult=DialogResult.Cancel};close.SetBounds(375,355,105,32);
  mode.Items[0]="Ligação por IP";mode.Items[1]="Porta série CAT";
  mode.SetBounds(24,48,372,30);fieldLabels[0].SetBounds(24,20,372,24);
  host.SetBounds(24,124,372,30);sl.SetBounds(24,196,372,30);
  com.SetBounds(24,124,250,30);refresh.SetBounds(286,124,110,30);speed.SetBounds(24,196,220,30);
  fieldLabels[1].SetBounds(24,96,372,24);fieldLabels[2].SetBounds(24,168,372,24);fieldLabels[3].SetBounds(24,96,372,24);fieldLabels[4].SetBounds(24,168,372,24);
  create.Text="Abrir SmartSDR CAT para criar uma porta";create.SetBounds(24,246,372,34);
  int contentWidth=ConfigurationContentWidth(dialog);
  Action enabled=delegate{bool cat=mode.SelectedIndex==1;dialog.SuspendLayout();host.Visible=sl.Visible=fieldLabels[1].Visible=fieldLabels[2].Visible=!cat;
   com.Visible=speed.Visible=refresh.Visible=create.Visible=fieldLabels[3].Visible=fieldLabels[4].Visible=cat;

   callLabel.SetBounds(24,cat?296:246,372,24);callInput.SetBounds(24,cat?324:274,372,30);
   languageLabel.SetBounds(24,cat?360:310,340,24);flags.SetBounds(24,cat?360:310,104,24);languagePicker.SetBounds(24,cat?386:336,372,30);
   note.Text=cat?"Usa uma porta CAT dedicada.\nA slice é escolhida no SmartSDR CAT.\nComunicação: 8 bits, sem paridade, 1 bit de paragem.":"O computador e o Flex devem estar na mesma rede.\nEscolhe a slice que queres acompanhar.";
   foreach(Control c in new Control[]{mode,host,sl,callLabel,callInput,languagePicker,create})c.Width=contentWidth;foreach(Label label in fieldLabels)label.Width=contentWidth;com.Width=contentWidth-122;refresh.Left=24+contentWidth-110;int rowTop=20;Action<Control,Control> placeField=delegate(Control label,Control input){label.SetBounds(24,rowTop,contentWidth,24);input.Top=rowTop+28;input.Height=30;rowTop=input.Bottom+20;};placeField(fieldLabels[0],mode);if(cat){placeField(fieldLabels[3],com);refresh.Top=com.Top;placeField(fieldLabels[4],speed);create.Top=rowTop;rowTop=create.Bottom+20;}else{placeField(fieldLabels[1],host);placeField(fieldLabels[2],sl);}placeField(callLabel,callInput);flags.SetBounds(24,rowTop,104,24);languagePicker.Top=rowTop+28;languagePicker.Height=30;rowTop=languagePicker.Bottom+20;note.Text=T(note.Text);note.Font=dialog.Font;int noteTop=rowTop;int noteHeight=Math.Max(48,TextRenderer.MeasureText(note.Text,note.Font,new Size(contentWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+8);note.SetBounds(24,noteTop,contentWidth,noteHeight);dialog.ClientSize=new Size(contentWidth+48,noteTop+noteHeight+66);
   about.SetBounds(24,dialog.ClientSize.Height-50,108,32);save.SetBounds(24+(contentWidth-108)/2,dialog.ClientSize.Height-50,108,32);close.SetBounds(24+contentWidth-108,dialog.ClientSize.Height-50,108,32);dialog.ResumeLayout();
  };mode.SelectedIndexChanged+=delegate{enabled();};enabled();
  Action translate=delegate{dialog.Text=T("Configuração")+" • Flex Meter";string[] keys={"Tipo de ligação","Endereço IP do Flex","Slice","Porta COM","Velocidade"};for(int i=0;i<keys.Length;i++)fieldLabels[i].Text=T(keys[i]);mode.Items[0]=T("Ligação por IP");mode.Items[1]=T("Porta série CAT");refresh.Text=T("Atualizar");create.Text=T("Abrir SmartSDR CAT para criar uma porta");callLabel.Text=T("Indicativo (opcional)");languageLabel.Text=T("Idioma");about.Text=T("Sobre");save.Text=T("Guardar");close.Text=T("Cancelar");enabled();};translate();
  languagePicker.SelectedIndexChanged+=delegate{string previous=language;try{SaveLanguage(new[]{"pt","es","en"}[languagePicker.SelectedIndex]);translate();}catch(IOException){language=previous;MessageBox.Show(dialog,T("Não foi possível guardar o idioma."));}catch(UnauthorizedAccessException){language=previous;MessageBox.Show(dialog,T("Não foi possível guardar o idioma."));}};
  save.Click+=delegate{IPAddress parsed;bool cat=mode.SelectedIndex==1;if(!cat&&!IPAddress.TryParse(host.Text.Trim(),out parsed)){MessageBox.Show(dialog,T("Introduz um IP válido."));return;}if(cat&&!Regex.IsMatch(com.Text.Trim(),@"^COM[1-9]\d*$",RegexOptions.IgnoreCase)){MessageBox.Show(dialog,T("Seleciona a porta COM do SmartSDR CAT."));return;}
   string newCall=NormalizeCallsign(callInput.Text);if(!ValidCallsign(newCall)){MessageBox.Show(dialog,T("Usa apenas letras, números, / ou - no indicativo."));return;}
   string saved="ip="+host.Text.Trim()+"\nslice="+sl.SelectedIndex+"\ncat="+(cat?"1":"0")+"\ncom="+com.Text.Trim().ToUpperInvariant()+"\nbaud="+speed.Text+"\ncallsign="+newCall+"\nlanguage="+language;
   try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));File.WriteAllText(SettingsPath,saved);}catch(Exception ex){MessageBox.Show(dialog,T("Não foi possível guardar: ")+ex.Message);return;}
   Disconnect();ip.Text=host.Text.Trim();slice.SelectedIndex=sl.SelectedIndex;catMode=cat;comPort=com.Text.Trim().ToUpperInvariant();baud=int.Parse(speed.Text);callsign=newCall;callsignConfigured=true;if(sharpBuffer!=null){sharpBuffer.Dispose();sharpBuffer=null;}Invalidate();if(aboutWindow!=null&&!aboutWindow.IsDisposed)aboutWindow.Close();dialog.Close();AutoConnect();};
  dialog.Controls.AddRange(new Control[]{mode,host,sl,com,refresh,speed,create,note,callLabel,callInput,languageLabel,languagePicker,flags,about,save,close});foreach(Control c in dialog.Controls){c.ForeColor=Color.FromArgb(241,245,255);c.BackColor=c is Label?dialog.BackColor:Color.FromArgb(3,22,40);}StyleConfiguration(dialog,save,note);enabled();dialog.AcceptButton=save;dialog.CancelButton=close;close.Click+=delegate{dialog.Close();};ShowIndependent(dialog,this);
 }
 public static bool ParseCat(string text,out double value){var m=Regex.Match(text,@"^ZZSM(\d{3})$");int raw;if(m.Success&&int.TryParse(m.Groups[1].Value,out raw)&&raw<=260){value=raw/2.0-140;return true;}value=-127;return false;}
 async void StartCat(){if(string.IsNullOrEmpty(comPort)){Configure();return;}cancel=new CancellationTokenSource();var token=cancel.Token;connect.Text="Desligar";connection="A abrir "+comPort+"…";catTime=DateTime.MinValue;
  var port=new SerialPort(comPort,baud,Parity.None,8,StopBits.One){Handshake=Handshake.None,DtrEnable=false,RtsEnable=false,ReadTimeout=600,WriteTimeout=600,NewLine=";"};serial=port;
  try{await Task.Run(delegate{port.Open();string buffer="";while(!token.IsCancellationRequested){port.Write("ZZSM;");var deadline=DateTime.UtcNow.AddMilliseconds(700);bool got=false;
    while(!token.IsCancellationRequested&&DateTime.UtcNow<deadline&&!got){string chunk=port.ReadExisting();buffer+=chunk;int p;while((p=buffer.IndexOf(';'))>=0){string frame=buffer.Substring(0,p).Trim();buffer=buffer.Substring(p+1);double value;if(ParseCat(frame,out value)){lock(gate){catValue=value;catTime=DateTime.UtcNow;}got=true;}}if(buffer.Length>4096)buffer="";if(!got)Thread.Sleep(15);}token.WaitHandle.WaitOne(100);
  }}); }catch(Exception ex){if(!token.IsCancellationRequested){Disconnect();connection="CAT: "+ex.Message;}}
 }
 bool ReadSignal(out double value){if(catMode){value=catValue;return DateTime.UtcNow-catTime<TimeSpan.FromSeconds(2);}return data.Reading(slice.SelectedIndex,out value);}
 [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
 protected override void WndProc(ref Message m){
  if(m.Msg==0x0084){base.WndProc(ref m);if(m.Result.ToInt32()==1){long packed=m.LParam.ToInt64();Point p=PointToClient(new Point(unchecked((short)(packed&0xffff)),unchecked((short)((packed>>16)&0xffff))));int grip=Math.Max(6,(int)(6*viewScale));bool left=p.X<grip,right=p.X>=ClientSize.Width-grip,top=p.Y<grip,bottom=p.Y>=ClientSize.Height-grip;
   int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:((p.Y-viewY)/viewScale<38?2:1);m.Result=new IntPtr(hit);}return;}
  const int WM_SIZING=0x0214;
  if(m.Msg==WM_SIZING){var r=(NativeRect)Marshal.PtrToStructure(m.LParam,typeof(NativeRect));int borderW=Width-ClientSize.Width,borderH=Height-ClientSize.Height;
   int edge=m.WParam.ToInt32();int w=Math.Max(1,r.Right-r.Left-borderW),h=Math.Max(1,r.Bottom-r.Top-borderH);
   // Left/right/corners follow width; top/bottom follow height.
   if(edge==3||edge==6)w=(int)Math.Round(h*500.0/340);else h=(int)Math.Round(w*340.0/500);
   int minW=Math.Max(1,MinimumSize.Width-borderW),minH=Math.Max(1,MinimumSize.Height-borderH);
   if(w<minW||h<minH){w=Math.Max(minW,(int)Math.Ceiling(minH*500.0/340));h=(int)Math.Round(w*340.0/500);}
   if(edge==1||edge==4||edge==7)r.Left=r.Right-w-borderW;else r.Right=r.Left+w+borderW;
   if(edge==3||edge==4||edge==5)r.Top=r.Bottom-h-borderH;else r.Bottom=r.Top+h+borderH;
   Marshal.StructureToPtr(r,m.LParam,false);m.Result=new IntPtr(1);return;
  }base.WndProc(ref m);
 }
 readonly object gate=new object(); MeterData data=new MeterData(); TcpClient tcp;UdpClient udp;CancellationTokenSource cancel;
 TextBox ip=new TextBox();Button connect=new LogbookButton(),demo=new LogbookButton(),hold=new LogbookButton();ComboBox slice=new ComboBox();
 Label state=new Label(); System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
 bool simulated=false;double needle=-127;PeakMemory peak=new PeakMemory();int ticks=0;readonly Stopwatch animationClock=Stopwatch.StartNew();double animationTime=0;DateTime liveSince;string connection="Desligado";
 string diagnostic="";
 readonly Dictionary<Control,Rectangle> layout=new Dictionary<Control,Rectangle>();
 float viewScale=1,viewX=0,viewY=0;Font controlFont;bool resizing=false;
 readonly Dictionary<int,Font> fonts=new Dictionary<int,Font>();
 Bitmap sharpBuffer;
 protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){if(sharpBuffer!=null){sharpBuffer.Dispose();sharpBuffer=null;}foreach(var f in fonts.Values)f.Dispose();fonts.Clear();}}
 void ResizeView(){if(layout.Count==0||resizing||IsDisposed||Disposing||WindowState==FormWindowState.Minimized||ClientSize.Width<=0||ClientSize.Height<=0)return;
  resizing=true;try{viewScale=Math.Max(.1f,Math.Min(ClientSize.Width/500f,ClientSize.Height/340f));viewX=(ClientSize.Width-500*viewScale)/2;viewY=(ClientSize.Height-340*viewScale)/2;
  SuspendLayout();int fontSize=Math.Max(10,(int)Math.Round(105*viewScale));if(!fonts.TryGetValue(fontSize,out controlFont)){controlFont=new Font("Arial",fontSize/10f,FontStyle.Regular,GraphicsUnit.Point);fonts[fontSize]=controlFont;}
  foreach(var entry in layout){var r=entry.Value;entry.Key.Font=controlFont;entry.Key.SetBounds((int)Math.Round(viewX+r.X*viewScale),(int)Math.Round(viewY+r.Y*viewScale),(int)Math.Round(r.Width*viewScale),(int)Math.Round(r.Height*viewScale));}
  ResumeLayout();Invalidate();}finally{resizing=false;}}
 public FlexMeter(){TopMost=true;Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);AutoScaleMode=AutoScaleMode.None;Text="Flex Meter 0.69 • FLEX-6400";StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.None;ClientSize=new Size(500,340);MinimumSize=new Size(328,223);MaximizeBox=false;ResizeRedraw=true;BackColor=Color.FromArgb(5,25,42);DoubleBuffered=true;Font=new Font("Arial",9);
  ip.Text="";
  var config=new LogbookButton{Text=T("Configuração"),Tag="config"};config.SetBounds(20,298,180,29);config.Click+=delegate{Configure();};Controls.Add(config);
  slice.Items.AddRange(new object[]{"Slice A (0)","Slice B (1)"});slice.SelectedIndex=0;
  hold.Text="Pico ON";hold.SetBounds(215,298,125,29);hold.Click+=delegate{peak.Hold=!peak.Hold;hold.Text=T(peak.Hold?"Pico OFF":"Pico ON");};Controls.Add(hold);
  var exit=new LogbookButton{Text=T("Sair"),Tag="exit"};exit.SetBounds(355,298,125,29);exit.Click+=delegate{Close();};Controls.Add(exit);
  slice.SelectedIndexChanged+=delegate{peak.Reset();needle=-127;};
  state.ForeColor=Color.WhiteSmoke;state.BackColor=Color.FromArgb(55,58,61);state.SetBounds(20,372,455,45);state.Visible=false;
  foreach(Control c in Controls){layout[c]=c.Bounds;c.ForeColor=Color.FromArgb(236,244,255);c.BackColor=Color.FromArgb(3,24,41);
   var button=c as Button;if(button!=null){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(32,82,116);button.FlatAppearance.BorderSize=1;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(12,64,105);button.FlatAppearance.MouseDownBackColor=Color.FromArgb(0,91,211);button.UseVisualStyleBackColor=false;
   }}
  connect.BackColor=Color.FromArgb(0,91,211);connect.FlatAppearance.BorderColor=Color.FromArgb(0,164,255);
  ip.BorderStyle=BorderStyle.FixedSingle;slice.FlatStyle=FlatStyle.Flat;
  Resize+=delegate{ResizeView();UpdateWindowShape();};ClientSize=new Size(750,510);ResizeView();UpdateWindowShape();
  timer.Interval=33;timer.Tick+=delegate{ticks++;double now=animationClock.Elapsed.TotalSeconds;double elapsed=Math.Min(.25,Math.Max(.001,now-animationTime));animationTime=now;{double target=-127; bool found=false;
   lock(gate){found=ReadSignal(out target);}
   if(simulated)target=-82+20*Math.Sin(ticks*.065)+8*Math.Sin(ticks*.19);else if(!found)target=-127;
   needle+=(target-needle)*(1-Math.Exp(-elapsed/(target>needle?.065:.15)));
   if(simulated||found)peak.Update(target,DateTime.UtcNow,elapsed);else peak.Reset();
  }state.Text=simulated?"DEMONSTRAÇÃO • valores simulados":connection;Invalidate();};timer.Start();
  FormClosing+=delegate{SaveWindow();closing=true;reconnectTimer.Stop();if(aboutWindow!=null&&!aboutWindow.IsDisposed)aboutWindow.Close();if(configurationWindow!=null&&!configurationWindow.IsDisposed)configurationWindow.Close();};
  FormClosed+=delegate{Disconnect();timer.Dispose();reconnectTimer.Dispose();};
  LoadSettings();ApplyLanguage();
  LoadWindow();
  reconnectTimer.Interval=4000;reconnectTimer.Tick+=delegate{AutoConnect();};
  Shown+=async delegate{if(!simulated){if(!callsignConfigured||Array.IndexOf(Environment.GetCommandLineArgs(),"--configure")>=0)Configure();AutoConnect();reconnectTimer.Start();await Task.Delay(5000);if(!closing&&!IsDisposed)await CheckForUpdates(false,this);}};
 }
 void UI(Action a){if(!IsDisposed&&IsHandleCreated)try{BeginInvoke(a);}catch(InvalidOperationException){}}
 void Disconnect(){if(cancel!=null){cancel.Cancel();cancel=null;}if(tcp!=null)tcp.Close();if(udp!=null)udp.Close();if(serial!=null){try{serial.Close();}catch(IOException){}serial=null;}catTime=DateTime.MinValue;peak.Reset();needle=-127;connect.Text="Ligar";connection="Desligado";lock(gate)data=new MeterData();}
 async void Start(string address){IPAddress host;if(!IPAddress.TryParse(address,out host)){MessageBox.Show("Introduz o endereço IP do FLEX-6400.");return;}
  simulated=false;demo.Text="Demo";cancel=new CancellationTokenSource();var token=cancel.Token;connect.Text="Desligar";connection="A ligar…";
  var client=new TcpClient();tcp=client;
  try{var pending=client.ConnectAsync(host,4992);if(await Task.WhenAny(pending,Task.Delay(5000,token))!=pending)throw new IOException("Tempo de ligação excedido.");await pending;if(token.IsCancellationRequested)return;
   var receiver=new UdpClient(new IPEndPoint(((IPEndPoint)client.Client.LocalEndPoint).Address,0));udp=receiver;liveSince=DateTime.UtcNow;
   var stream=client.GetStream();int port=((IPEndPoint)receiver.Client.LocalEndPoint).Port;
   var replies=new Dictionary<int,TaskCompletionSource<string>>();int seq=0;object replyGate=new object();
   var readTask=Task.Run(delegate{using(var reader=new StreamReader(stream,Encoding.ASCII)){string line;while(!token.IsCancellationRequested&&(line=reader.ReadLine())!=null){lock(gate)data.Line(line);Match response=Regex.Match(line,@"^R(\d+)\|([^|]+)\|(.*)");if(response.Success){TaskCompletionSource<string> waiter;lock(replyGate){if(replies.TryGetValue(int.Parse(response.Groups[1].Value),out waiter))waiter.TrySetResult(response.Groups[2].Value+"|"+response.Groups[3].Value);}}}}});
   Func<string,Task> command=async delegate(string cmd){var completion=new TaskCompletionSource<string>();int id=++seq;lock(replyGate)replies[id]=completion;
    try{byte[] bytes=Encoding.ASCII.GetBytes("C"+id+"|"+cmd+"\n");await stream.WriteAsync(bytes,0,bytes.Length);if(await Task.WhenAny(completion.Task,Task.Delay(3000,token))!=completion.Task)throw new IOException("Sem confirmação: "+cmd);
     string result=await completion.Task;if(result.Split('|')[0].TrimStart('0').Length!=0)throw new IOException("Comando recusado: "+cmd+" • "+result);
    }finally{lock(replyGate)replies.Remove(id);}};
   string[] cmds={"sub slice all","ping","client udpport "+port,"meter list","sub meter all"};
   foreach(string cmd in cmds)await command(cmd);
   diagnostic="UDP "+port;
   connection="Ligado a "+address+" • à espera de leituras. Abre uma slice no SmartSDR.";
   var receiveTask=Task.Run(async delegate {try{while(!token.IsCancellationRequested){var packet=await receiver.ReceiveAsync();if(packet.RemoteEndPoint.Address.Equals(host))lock(gate)data.Packet(packet.Buffer);}}catch(Exception ex){if(!token.IsCancellationRequested)UI(delegate{connection="Erro UDP: "+ex.Message;});}});
   while(!token.IsCancellationRequested&&!readTask.IsCompleted){await Task.WhenAny(readTask,Task.Delay(2000,token));if(!token.IsCancellationRequested&&!readTask.IsCompleted)await command("ping");}
   await readTask;
   if(!token.IsCancellationRequested)throw new IOException("O rádio terminou a ligação.");
  }catch(Exception ex){if(!token.IsCancellationRequested){Disconnect();connection="Ligação: "+ex.Message;}}
 }
 public static double Microvolts(double dbm){return Math.Sqrt(50*Math.Pow(10,(dbm-30)/10))*1000000;}
 static PointF P(double angle,double r){double a=angle*Math.PI/180;return new PointF(250+(float)(Math.Cos(a)*r),408+(float)(Math.Sin(a)*r));}
 public static double SignalAngle(double dbm){return 234+Math.Max(0,Math.Min(114,dbm+127))*72/114;}
 public static string SignalText(double dbm){if(dbm>-73){int over=(int)Math.Round(dbm+73,MidpointRounding.AwayFromZero);return over>0?"S9+"+over:"S9";}return "S"+Math.Max(0,Math.Min(9,(int)Math.Round((dbm+127)/6,MidpointRounding.AwayFromZero)));}
 void Center(Graphics g,string text,Font f,Brush b,float x,float y){SizeF s=g.MeasureString(text,f);g.DrawString(text,f,b,x-s.Width/2,y);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(WindowState==FormWindowState.Minimized||ClientSize.Width<=0||ClientSize.Height<=0)return;
  // Cache the static dial at native size after one supersampled render per resize.
  int w=ClientSize.Width,h=ClientSize.Height;
  if(sharpBuffer==null||sharpBuffer.Width!=w||sharpBuffer.Height!=h){if(sharpBuffer!=null)sharpBuffer.Dispose();sharpBuffer=new Bitmap(w,h,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
   using(var highRes=new Bitmap(w*3,h*3,System.Drawing.Imaging.PixelFormat.Format32bppPArgb)){
    using(var surface=Graphics.FromImage(highRes)){surface.Clear(BackColor);surface.ScaleTransform(3,3);RenderMeter(surface);}
    using(var output=Graphics.FromImage(sharpBuffer)){output.InterpolationMode=InterpolationMode.HighQualityBicubic;output.PixelOffsetMode=PixelOffsetMode.HighQuality;output.DrawImage(highRes,new Rectangle(0,0,w,h),0,0,w*3,h*3,GraphicsUnit.Pixel);}
   }
  }
  e.Graphics.DrawImageUnscaled(sharpBuffer,0,0);RenderDynamic(e.Graphics);
 }
 void RenderMeter(Graphics g){var canvas=g.Save();g.TranslateTransform(viewX,viewY);g.ScaleTransform(viewScale,viewScale);g.SmoothingMode=SmoothingMode.HighQuality;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
  using(var background=FrameGradient(new Rectangle(0,0,500,340)))g.FillRectangle(background,0,0,500,340);
  using(var edge=new LinearGradientBrush(new Rectangle(0,0,500,340),Color.FromArgb(86,150,195),Color.FromArgb(14,48,72),90f))using(var border=new Pen(edge,1))using(var outline=RoundedFrame(new RectangleF(1,1,498,338),FrameCornerRadius))g.DrawPath(border,outline);
  using(var f=new Font("Arial",12,FontStyle.Regular))Center(g,"FLEX METER"+(callsign.Length>0?" — "+callsign:""),f,Brushes.WhiteSmoke,250,4);
  g.TranslateTransform(0,-12);
  using(var accent=new Pen(Color.FromArgb(0,154,255),2))g.DrawLine(accent,24,45,476,45);
  using(var b=new SolidBrush(Color.FromArgb(3,24,41)))g.FillRectangle(b,24,52,452,244);
  using(var border=new Pen(Color.FromArgb(30,75,105),1))g.DrawRectangle(border,24,52,452,244);
  float s9=(float)SignalAngle(-73);
  using(var white=new Pen(Color.WhiteSmoke,2))g.DrawArc(white,-60,98,620,620,234,s9-234);
  using(var red=new Pen(Color.FromArgb(239,43,53),2))g.DrawArc(red,-60,98,620,620,s9,306-s9);
  using(var f=new Font("Arial",11,FontStyle.Regular))for(int i=0;i<=15;i++){double level=i<=9?-127+i*6:-73+(i-9)*10;double a=SignalAngle(level);bool over=i>9;using(var p=new Pen(over?Color.FromArgb(239,43,53):Color.WhiteSmoke,2))g.DrawLine(p,P(a,310),P(a,321));PointF t=P(a,341);Center(g,i<=9?i.ToString():((i-9)*10).ToString(),f,over?SignalRedBrush:Brushes.WhiteSmoke,t.X,t.Y-9);}
  using(var f=new Font("Arial",10,FontStyle.Regular)){Center(g,T("Unidades S"),f,Brushes.SlateGray,250,166);Center(g,T("dB acima do S9"),f,SignalRedBrush,250,189);}
  g.Restore(canvas);
 }
 void RenderDynamic(Graphics g){var canvas=g.Save();g.TranslateTransform(viewX,viewY);g.ScaleTransform(viewScale,viewScale);g.TranslateTransform(0,-12);g.SmoothingMode=SmoothingMode.HighQuality;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
  bool valid=false;double current;lock(gate){valid=ReadSignal(out current);}
  if(valid)connection=catMode?"Ligado por CAT • "+comPort:"Ligado a "+ip.Text+" • Slice "+(slice.SelectedIndex==0?"A":"B");
  else if(cancel!=null&&catMode)connection="CAT • "+comPort+" • à espera de sinal (ZZSM).";
  else if(cancel!=null&&DateTime.UtcNow-liveSince>TimeSpan.FromSeconds(5))lock(gate)connection="Sem leitura da slice • "+diagnostic+" • pacotes "+data.Packets+" / meter "+data.MeterPackets+" / descrições "+data.Meta.Count;
  double angle=SignalAngle(needle);
  double peakAngle=SignalAngle(peak.Value);
  var saved=g.Save();g.SetClip(new Rectangle(27,53,446,198));
  using(var pen=new Pen(Color.FromArgb(239,43,53),3))g.DrawLine(pen,P(peakAngle,0),P(peakAngle,319));
  using(var pen=new Pen(Color.WhiteSmoke,2))g.DrawLine(pen,P(angle,0),P(angle,319));g.Restore(saved);
  using(var f=new Font("Consolas",17,FontStyle.Regular))using(var format=new StringFormat(StringFormat.GenericTypographic)){
   format.FormatFlags|=StringFormatFlags.NoWrap;format.Alignment=StringAlignment.Near;
   string reading=valid||simulated?SignalText(peak.Value):"—";int plus=reading.IndexOf('+');
   string baseText=plus>=0?reading.Substring(0,plus):reading;string extra=plus>=0?"+"+reading.Substring(plus+1).PadLeft(2,'0'):"";
   float cell=g.MeasureString("0",f,PointF.Empty,format).Width;float left=250-cell*2.5f;
   g.DrawString(baseText,f,Brushes.WhiteSmoke,new PointF(left,255),format);
   g.DrawString(extra,f,Brushes.WhiteSmoke,new PointF(left+cell*2,255),format);
  }
  using(var f=new Font("Consolas",10,FontStyle.Regular))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap}){
   bool hasReading=valid||simulated;double microvolts=Microvolts(peak.Value);
   string voltage=hasReading?microvolts.ToString(microvolts<10?"0.00":microvolts<1000?"0.0":"0",CultureInfo.CurrentCulture)+" µV":"— µV";
   string power=hasReading?peak.Value.ToString("0.0",CultureInfo.CurrentCulture)+" dBm":"— dBm";
   format.Alignment=StringAlignment.Near;g.DrawString(voltage,f,Brushes.WhiteSmoke,new RectangleF(34,264,144,30),format);
   format.Alignment=StringAlignment.Far;g.DrawString(power,f,Brushes.WhiteSmoke,new RectangleF(322,264,144,30),format);
  }
  if(simulated)using(var f=new Font("Arial",9))g.DrawString("DEMO",f,Brushes.Gold,38,247);
  g.Restore(canvas);
 }
 [STAThread]static void Main(string[] args){Application.EnableVisualStyles();if(args.Length>1&&args[0]=="--preview"){using(var f=new FlexMeter()){if(args.Length>3)f.ClientSize=new Size(int.Parse(args[2]),int.Parse(args[3]));f.simulated=true;f.callsign="";f.ip.Text="";f.needle=-82;f.peak.Value=-64;f.timer.Stop();f.Show();Application.DoEvents();using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1]);}f.Close();}return;}Application.Run(new FlexMeter());}
}




























