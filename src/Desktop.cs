using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.Media.Ocr;

namespace WwmRedeemer {
 public static class Desktop {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public InputUnion u; }
  [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public Mouse mouse; [FieldOffset(0)] public Keyboard key; }
  [StructLayout(LayoutKind.Sequential)] struct Mouse { public int x,y; public uint data,flags,time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
  [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] input,int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int n);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h,ref Point p);
  [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")]static extern uint MapVirtualKey(uint code,uint type);
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")] static extern short GetKeyState(int key);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
  public static volatile bool Stop;
  public static void Check() { if(Stop || (GetAsyncKeyState(0x79)&0x8000)!=0) {Stop=true;throw new OperationCanceledException("已停止（F10）。");} }
  public static void Guard(IntPtr h) { Check(); if(GetForegroundWindow()!=h) throw new InvalidOperationException("視窗焦點已改變，已暫停，避免輸入到其他程式。"); }
  public static async Task Focus(IntPtr h) { Check(); ShowWindow(h,9);SetForegroundWindow(h);await Task.Delay(450);Guard(h); }
  public static IntPtr Find(string process) {
   var candidates=Process.GetProcessesByName(process).Where(p=>p.MainWindowHandle!=IntPtr.Zero).ToList();
   if(candidates.Count!=1) throw new InvalidOperationException("請開啟唯一的 "+process+" 視窗（目前 "+candidates.Count+" 個）。");
   Privileges.RequireInput(candidates[0].Id);
   return candidates[0].MainWindowHandle;
  }
  public static Rectangle Bounds(IntPtr h) {
   Rect r;Point p=new Point();if(!GetClientRect(h,out r)||!ClientToScreen(h,ref p)||r.R<500||r.B<300) throw new InvalidOperationException("無法取得視窗大小。");
   return new Rectangle(p.X,p.Y,r.R,r.B);
  }
  static void Send(Input i) { if(SendInput(1,new[]{i},Marshal.SizeOf(typeof(Input)))!=1) throw new InvalidOperationException("Windows 拒絕輸入（錯誤 "+Marshal.GetLastWin32Error()+"），請確認遊戲與助手的權限等級相同。"); }
  static void KeyEvent(ushort vk,bool up,ushort scan=0,uint flags=0) {if(vk!=0){scan=(ushort)MapVirtualKey(vk,0);vk=0;flags|=8;}Send(new Input{type=1,u=new InputUnion{key=new Keyboard{vk=vk,scan=scan,flags=flags|(up?2u:0u)}}}); }
  public static void Key(IntPtr h,ushort vk) { Guard(h); KeyEvent(vk,false);Thread.Sleep(35);KeyEvent(vk,true); }
  static void VirtualKey(ushort vk,bool up){Send(new Input{type=1,u=new InputUnion{key=new Keyboard{vk=vk,flags=up?2u:0u}}});}
  static void Chord(IntPtr h,ushort vk){Guard(h);VirtualKey(0x11,false);try{Thread.Sleep(70);VirtualKey(vk,false);Thread.Sleep(70);VirtualKey(vk,true);}finally{VirtualKey(0x11,true);}Thread.Sleep(100);}
  public static void Clear(IntPtr h) {Chord(h,0x41);Guard(h);VirtualKey(0x08,false);Thread.Sleep(70);VirtualKey(0x08,true);Thread.Sleep(100);}
  public static void Type(IntPtr h,string text) {
   Guard(h);Exception error=null;
   var thread=new Thread(()=>{try{System.Windows.Forms.Clipboard.SetText(text);}catch(Exception ex){error=ex;}});
   thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();
   if(!thread.Join(2000))throw new TimeoutException("剪貼簿忙碌，尚未送出。");if(error!=null)throw error;
   Chord(h,0x56);
  }
  public static void TypeUnicode(IntPtr h,string text) {
   // VK_PACKET/WM_CHAR text input; deliberately independent of keyboard layout/IME.
   foreach(char c in text){Guard(h);KeyEvent(0,false,c,4);KeyEvent(0,true,c,4);Thread.Sleep(65);}
  }
  static T ClipboardAction<T>(Func<T> action){
   T result=default(T);Exception error=null;
   var thread=new Thread(()=>{try{result=action();}catch(Exception ex){error=ex;}});
   thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();
   if(!thread.Join(2000))throw new TimeoutException("剪貼簿忙碌。");if(error!=null)throw error;return result;
  }
  public static bool ReadBackMatches(IntPtr h,string expected){
   Guard(h);string marker="wwm-probe-"+Guid.NewGuid().ToString("N");
   try {
    ClipboardAction(()=>{System.Windows.Forms.Clipboard.SetText(marker);return true;});
    Chord(h,0x41);Chord(h,0x43);Thread.Sleep(200);Guard(h);
    string copied=ClipboardAction(()=>System.Windows.Forms.Clipboard.GetText());
    return copied!=marker && String.Equals(copied,expected,StringComparison.OrdinalIgnoreCase);
   }catch(ExternalException){return false;}
   finally{ClipboardAction(()=>{System.Windows.Forms.Clipboard.SetText(expected);return true;});}
  }
  public static void TypeKeys(IntPtr h,string text) {
   if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"^[A-Za-z0-9]+$"))throw new InvalidOperationException("此碼無法使用鍵盤備援輸入。");
   foreach(char c in text){
    Guard(h);bool shift=Char.IsLetter(c)&&(Char.IsUpper(c)!=((GetKeyState(0x14)&1)!=0));
    if(shift)KeyEvent(0x10,false);
    try{Key(h,(ushort)Char.ToUpperInvariant(c));}finally{if(shift)KeyEvent(0x10,true);}
    Thread.Sleep(45);
   }
  }
  public static void Click(IntPtr h,int x,int y) {
   Guard(h);var b=Bounds(h);var p=new Point{X=b.X+x,Y=b.Y+y};
   if(x<0||y<0||x>=b.Width||y>=b.Height||GetAncestor(WindowFromPoint(p),2)!=h) throw new InvalidOperationException("目標位置被其他視窗遮住。");
   Move(p);Send(new Input{type=0,u=new InputUnion{mouse=new Mouse{flags=2}}});Thread.Sleep(70);Send(new Input{type=0,u=new InputUnion{mouse=new Mouse{flags=4}}});
  }
  static void Move(Point p) {
   int x=(int)Math.Round((p.X-GetSystemMetrics(76))*65535.0/(GetSystemMetrics(78)-1));int y=(int)Math.Round((p.Y-GetSystemMetrics(77))*65535.0/(GetSystemMetrics(79)-1));
   Send(new Input{type=0,u=new InputUnion{mouse=new Mouse{x=x,y=y,flags=0xC001}}});Thread.Sleep(100);
  }
  public static void Wheel(IntPtr h,int x,int y,int amount) {Guard(h);var b=Bounds(h);var p=new Point{X=b.X+x,Y=b.Y+y};if(GetAncestor(WindowFromPoint(p),2)!=h)throw new InvalidOperationException("Discord 訊息區被遮住。");Move(p);Send(new Input{type=0,u=new InputUnion{mouse=new Mouse{flags=0x800,data=unchecked((uint)amount)}}});}
  public static byte[] Capture(IntPtr h) {
   Guard(h);var b=Bounds(h);
   using(var bitmap=new Bitmap(b.Width,b.Height))using(var g=Graphics.FromImage(bitmap))using(var s=new MemoryStream()) {
    g.CopyFromScreen(b.X,b.Y,0,0,b.Size);bitmap.Save(s,ImageFormat.Png);return s.ToArray();
   }
  }
  public static string Compact(string s) {return System.Text.RegularExpressions.Regex.Replace(s??"",@"\s+","");}
  public static string Text(OcrResult o) {return String.Join("\n",o.Lines.Select(l=>l.Text));}
  public static Rectangle? Locate(OcrResult o,string label,Rectangle? area=null) {
   foreach(var l in o.Lines) if(Compact(l.Text).Contains(label) && l.Words.Count>0) {
    for(int start=0;start<l.Words.Count;start++)for(int length=1;length<=l.Words.Count-start;length++){
     var words=l.Words.Skip(start).Take(length).ToList();if(!Compact(String.Join("",words.Select(w=>w.Text))).Contains(label))continue;
     var left=words.Min(w=>w.BoundingRect.Left);var top=words.Min(w=>w.BoundingRect.Top);
     var right=words.Max(w=>w.BoundingRect.Right);var bottom=words.Max(w=>w.BoundingRect.Bottom);
     var found=new Rectangle((int)left,(int)top,(int)(right-left),(int)(bottom-top));
     if(!area.HasValue||area.Value.Contains(found.Left+found.Width/2,found.Top+found.Height/2))return found;
    }
   } return null;
  }
  public class UiNode {public string Id,Name;public ControlType Type;public Rectangle Rect;}
  public class DiscordPage {
   public List<SourceMessage> Messages=new List<SourceMessage>();public List<string> Ids=new List<string>();
   public Rectangle Pane;public bool AtStart;public string Fingerprint;public List<UiNode> Nodes;
  }
  public static List<UiNode> Snapshot(IntPtr h) {
   for(int attempt=0;;attempt++){
    Check();try{return SnapshotOnce(h);}
    catch(COMException){if(attempt>=7)throw;Thread.Sleep(350);}
    catch(ElementNotAvailableException){if(attempt>=7)throw;Thread.Sleep(350);}
   }
  }
  static List<UiNode> SnapshotOnce(IntPtr h) {
   Guard(h);var request=new CacheRequest();request.TreeScope=TreeScope.Element;
   request.Add(AutomationElement.NameProperty);request.Add(AutomationElement.AutomationIdProperty);
   request.Add(AutomationElement.ControlTypeProperty);request.Add(AutomationElement.BoundingRectangleProperty);
   using(request.Activate()) {
    var elements=AutomationElement.FromHandle(h).FindAll(TreeScope.Descendants,Condition.TrueCondition);
    var result=new List<UiNode>();
    foreach(AutomationElement e in elements){var c=e.Cached;var r=c.BoundingRectangle;
     result.Add(new UiNode{Id=c.AutomationId??"",Name=c.Name??"",Type=c.ControlType,Rect=r.IsEmpty?Rectangle.Empty:new Rectangle((int)r.X,(int)r.Y,(int)r.Width,(int)r.Height)});
    }return result;
   }
  }
  public static DiscordPage ReadDiscord(IntPtr h) {
   var all=Snapshot(h);var ids=all.Where(e=>e.Id.StartsWith("chat-messages-")).ToList();
   if(ids.Any(e=>!e.Id.StartsWith("chat-messages-"+Store.Channel+"-")))throw new InvalidOperationException("Discord 開啟的不是指定的兌換碼討論串。");
   if(ids.Count==0)throw new InvalidOperationException("尚未讀到指定 Discord 討論串的訊息。");
   var list=all.Where(e=>e.Type==ControlType.List&&e.Rect.Width>0&&e.Rect.Height>0&&ids.Any(n=>!n.Rect.IsEmpty&&e.Rect.Contains(n.Rect.Left+n.Rect.Width/2,n.Rect.Top+n.Rect.Height/2))).OrderBy(e=>(long)e.Rect.Width*e.Rect.Height).FirstOrDefault();
   if(list==null)throw new InvalidOperationException("找不到訊息捲動區。");
   var page=new DiscordPage{Nodes=all,Pane=Rectangle.Intersect(list.Rect,Bounds(h)),AtStart=ids.Any(e=>e.Id=="chat-messages-"+Store.Channel+"-"+Store.Channel)};
   for(int i=0;i<all.Count;i++){
    if(!all[i].Id.StartsWith("chat-messages-"))continue;
    string id=all[i].Id.Split('-').Last();page.Ids.Add(id);int end=i+1;
    while(end<all.Count&&!all[end].Id.StartsWith("chat-messages-"))end++;
    var descendants=all.Skip(i+1).Take(end-i-1).ToList();
    var body=descendants.Select(n=>n.Name).FirstOrDefault(n=>n.Contains(" , "));
    if(body!=null)body=MessageBody(body);
    // Forwarded cards can have an empty accessible message name. Read their text nodes.
    if(String.IsNullOrWhiteSpace(body))body=String.Join("\n",descendants.Where(n=>n.Type==ControlType.Text&&Store.Extract(n.Name).Count>0).Select(n=>n.Name).Distinct());
    page.Messages.Add(new SourceMessage{Id=id,Text=body??""});
   }
   page.Fingerprint=String.Join("|",ids.Where(n=>n.Rect.IntersectsWith(page.Pane)).Select(n=>n.Id+":"+(n.Rect.Y-page.Pane.Y)));
   if(page.Fingerprint=="")page.Fingerprint=String.Join("|",page.Ids);
   return page;
  }
  public static bool ClickDiscordButton(IntPtr h,DiscordPage page,string name) {
   var matches=page.Nodes.Where(n=>n.Type==ControlType.Button&&n.Name==name&&n.Rect.Width>0).ToList();
   if(matches.Count!=1)return false;var r=matches[0].Rect;var b=Bounds(h);Click(h,r.X-b.X+r.Width/2,r.Y-b.Y+r.Height/2);return true;
  }
  public static string MessageBody(string name){int a=name.IndexOf(" , "),z=name.LastIndexOf(" , ");return a>=0&&z>=a+3?name.Substring(a+3,z-a-3):"";}
  public static void ScrollDiscord(IntPtr h,DiscordPage page,int direction) {
   var b=Bounds(h);var r=page.Pane;if(r.Width<100||r.Height<100)throw new InvalidOperationException("訊息區尺寸無法確認。");
   Wheel(h,r.X-b.X+r.Width-35,r.Y-b.Y+r.Height/2,direction*480);
  }
 }
}
