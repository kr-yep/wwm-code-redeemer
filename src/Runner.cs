using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WwmRedeemer {
 public class Runner : IRedemptionClient {
  readonly Store store; readonly Action<string> log;
  IntPtr game; Rectangle dialogTitle; string activeCode;
  public Runner(Store store,Action<string> log) {this.store=store;this.log=log;}
  int IngestScanPage(IEnumerable<SourceMessage> messages,string checkpoint){
   int changed=0;foreach(var message in messages){
    if(!String.IsNullOrEmpty(checkpoint)&&UInt64.Parse(message.Id)<UInt64.Parse(checkpoint))continue;
    if(store.Ingest(message))changed++;
   }return changed;
  }
  public async Task Scan() {
   string checkpoint=store.ScanCheckpointId;bool incremental=!String.IsNullOrEmpty(checkpoint);
   store.ScanStatus(DateTime.Now.ToString("yyyy-MM-dd HH:mm")+(incremental?" 增量":" 完整")+"文字掃描進行中；尚未確認完整。");
   IntPtr h=Desktop.Find("Discord");await Desktop.Focus(h);
   var current=Desktop.ReadDiscord(h);IngestScanPage(current.Messages,checkpoint);
   if(Desktop.ClickDiscordButton(h,current,"跳到至當前"))await Task.Delay(1300);
   current=Desktop.ReadDiscord(h);
   // Reach a stable visible bottom, rather than mistaking unchanged buffered IDs for the end.
   int stable=0;string last="";
   for(int i=0;i<160;i++){
    IngestScanPage(current.Messages,checkpoint);
    stable=current.Fingerprint==last?stable+1:0;last=current.Fingerprint;
    if(stable>=3)break;
    Desktop.ScrollDiscord(h,current,-1);await Task.Delay(550);current=Desktop.ReadDiscord(h);
    if(i==159)throw new InvalidOperationException("未到達最新訊息，保留部分資料。");
   }
   string target=current.Ids.Max();IngestScanPage(current.Messages,checkpoint);
   log("已核對最新訊息："+target+(incremental?"；向前接回上次進度 "+checkpoint+"。":"；首次完整文字掃描。"));
   if(!incremental){
    if(!Desktop.ClickDiscordButton(h,current,"跳到頂部"))throw new InvalidOperationException("找不到討論串的跳到頂部按鈕。");
    for(int i=0;i<15;i++){await Task.Delay(450);current=Desktop.ReadDiscord(h);if(current.AtStart)break;}
    if(!current.AtStart)throw new InvalidOperationException("未能核對討論串起始訊息，不能宣稱完整。");
   }
   var coverage=new ScanCoverage(incremental?checkpoint:Store.Channel,target);stable=0;last="";
   for(int page=0;page<1600;page++){
    Desktop.Check();
    coverage.Add(current.Ids);
    int changed=IngestScanPage(current.Messages,checkpoint);
    int relevant=coverage.Seen.Count(id=>!incremental||UInt64.Parse(id)>UInt64.Parse(checkpoint));
    if(page%5==0||changed>0)log((incremental?"增量":"完整")+"掃描 "+(page+1)+"：已核對 "+relevant+" 則"+(incremental?"進度之後的訊息":"訊息")+"；累計 "+store.Codes.Count+" 組碼。");
    if(coverage.Complete)break;
    stable=current.Fingerprint==last?stable+1:0;last=current.Fingerprint;
    if(stable>=6)throw new InvalidOperationException("捲動未前進，僅完成部分掃描；尚未核對到最新訊息。");
    Desktop.ScrollDiscord(h,current,incremental?1:-1);await Task.Delay(500);current=Desktop.ReadDiscord(h);
   }
   if(!coverage.Complete)throw new InvalidOperationException("已達掃描上限，資料已保存但尚未接回歷史；進度不會前移。");
   var relevantIds=coverage.Seen.Where(id=>!incremental||UInt64.Parse(id)>UInt64.Parse(checkpoint)).ToList();
   var summary=relevantIds.Count==0?"沒有新增訊息":relevantIds.Select(id=>store.Messages[id].Published).Min()+" ～ "+relevantIds.Select(id=>store.Messages[id].Published).Max()+"；本次 "+relevantIds.Count+" 則";
   store.CompleteScan(DateTime.Now.ToString("yyyy-MM-dd HH:mm")+(incremental?" 增量文字掃描已核對；":" 完整文字掃描已核對；")+summary+"；最新 ID "+target,target);
   log(store.LastScan);
  }
  class GameScreen {
   public int Width,Height;
   public Windows.Media.Ocr.OcrResult Normal,Contrast;
   public string Text {get{return Desktop.Text(Normal)+"\n"+Desktop.Text(Contrast);}}
   public Rectangle? Find(string label){return Desktop.Locate(Contrast,label)??Desktop.Locate(Normal,label);}
   public Rectangle? Title {get{var area=new Rectangle((int)(Width*.3),(int)(Height*.3),(int)(Width*.4),(int)(Height*.35));return Desktop.Locate(Contrast,"兌換獎勵",area)??Desktop.Locate(Normal,"兌換獎勵",area)??Desktop.Locate(Contrast,"換獎勵",area)??Desktop.Locate(Normal,"換獎勵",area);}}
   public Rectangle? ExchangeRow {get{var area=new Rectangle(0,(int)(Height*.15),(int)(Width*.6),(int)(Height*.3));return Desktop.Locate(Normal,"兌換碼",area)??Desktop.Locate(Contrast,"兌換碼",area)??Desktop.Locate(Normal,"換碼",area)??Desktop.Locate(Contrast,"換碼",area)??Desktop.Locate(Normal,"换码",area)??Desktop.Locate(Contrast,"换码",area);}}
   public bool Settings {get{return !Title.HasValue&&ExchangeRow.HasValue&&Desktop.Compact(Text).Contains("帳戶資訊");}}
  }
  async Task<GameScreen> ReadScreen(IntPtr h){return await ReadScreenshot(Desktop.Capture(h));}
  static async Task<GameScreen> ReadScreenshot(byte[] shot){
   int width,height;using(var stream=new MemoryStream(shot))using(var bitmap=new Bitmap(stream)){width=bitmap.Width;height=bitmap.Height;}
   return new GameScreen{Width=width,Height=height,Normal=await Ocr.Read(shot),Contrast=await Ocr.Read(Ocr.Contrast(shot))};
  }
  public static async Task<string> InspectScreenImage(byte[] shot){
   var screen=await ReadScreenshot(shot);return "Settings: "+screen.Settings+"\nTitle: "+screen.Title+"\nExchangeRow: "+screen.ExchangeRow+"\n"+screen.Text;
  }
  public static async Task<string> InspectInputImage(byte[] shot,string code){
   var screen=await ReadScreenshot(shot);if(!screen.Title.HasValue)throw new InvalidOperationException("圖片未識別到中央兌換標題。");
   var title=screen.Title.Value;int y=title.Top+title.Height/2+(int)(screen.Height*.080);
   var area=InputArea(screen.Width,screen.Height,y);var cropped=Crop(shot,area);string report="Title: "+title+"\nExchangeRow: "+screen.ExchangeRow+"\nArea: "+area+"\n";bool matched=false;
   for(int factor=1;factor<=3;factor++){
    var text=Desktop.Text(await Ocr.Read(factor==1?cropped:Ocr.Scale(cropped,factor),"en-US"));report+="Scale "+factor+": "+text+"\n";matched|=InputMatches(text,code);
   }
   return (matched?"MATCH":"NO MATCH")+"\n"+report;
  }
  static Rectangle InputArea(int width,int height,int y){return new Rectangle((int)(width*.335),y-(int)(height*.023),(int)(width*.345),(int)(height*.046));}
  async Task EnsureDialog(IntPtr h) {
   var screen=await ReadScreen(h);if(screen.Title.HasValue){dialogTitle=screen.Title.Value;return;}
   var row=screen.ExchangeRow;
   if(!screen.Settings || !row.HasValue)throw new InvalidOperationException("請將遊戲停在「設定 → 其他 → 兌換碼」頁面。");
   log("開啟兌換輸入框。");var b=Desktop.Bounds(h);Desktop.Click(h,(int)(b.Width*.54),row.Value.Top+row.Value.Height/2);
   for(int i=0;i<8;i++){await Task.Delay(300);screen=await ReadScreen(h);if(screen.Title.HasValue){dialogTitle=screen.Title.Value;return;}}
   File.WriteAllBytes(Path.Combine(store.Folder,"last-dialog-open.png"),Desktop.Capture(h));
   File.WriteAllText(Path.Combine(store.Folder,"last-dialog-open.txt"),"Selected left row: "+row.Value+"\n"+screen.Text);
   throw new InvalidOperationException("未能開啟兌換輸入框，停止，尚未送出。");
  }
  public async Task VerifyAccount(string profile) {
   var h=Desktop.Find("wwm");await Desktop.Focus(h);var shot=Desktop.Capture(h);
   var observed=await InspectAccountImage(shot,profile);
   if(observed.StartsWith("MATCH\n"))return;
   if((await ReadScreenshot(shot)).Settings){
    log("設定頁角色 ID 辨識不清，開啟兌換框後重新確認。");
    await EnsureDialog(h);await Task.Delay(300);shot=Desktop.Capture(h);
    observed=await InspectAccountImage(shot,profile);
    if(observed.StartsWith("MATCH\n"))return;
   }
   File.WriteAllBytes(Path.Combine(store.Folder,"last-account-check.png"),shot);
   File.WriteAllText(Path.Combine(store.Folder,"last-account-check.txt"),observed);
   throw new InvalidOperationException("無法在遊戲畫面確認角色 ID "+profile+"，尚未送出兌換。請停在設定頁並讓左下角 ID 可見；檢查截圖已保存。");
  }
  public static async Task<string> InspectAccountImage(byte[] shot,string profile){
   string observed=Desktop.Text(await Ocr.Read(shot));
   if(AccountMatches(observed,profile))return "MATCH\n"+observed;
   int width,height;using(var stream=new MemoryStream(shot))using(var bitmap=new Bitmap(stream)){width=bitmap.Width;height=bitmap.Height;}
   var region=Crop(shot,new Rectangle(0,(int)(height*.96),(int)(width*.25),height-(int)(height*.96)));
   foreach(int factor in new[]{1,2,3,4}){
    var scaled=factor==1?region:Ocr.Scale(region,factor);
    foreach(var variant in new[]{scaled,Ocr.Contrast(scaled)}){
     var text=Desktop.Text(await Ocr.Read(variant,"en-US"));observed+="\nScale "+factor+": "+text;
     if(AccountMatches(text,profile))return "MATCH\n"+observed;
    }
   }
   return "NO MATCH\n"+observed;
  }
  public static bool AccountMatches(string observed,string profile){return System.Text.RegularExpressions.Regex.IsMatch(Desktop.Compact(observed),@"(?<!\d)"+System.Text.RegularExpressions.Regex.Escape(profile)+@"(?!\d)");}
  public async Task Redeem(DateTime? since,int limit) {
   game=Desktop.Find("wwm");await Desktop.Focus(game);
   await RedemptionWorkflow.Run(store,this,since,limit,log,Desktop.Check);
  }
  public async Task TestInput(DateTime? since) {
   var item=store.Pending(since).FirstOrDefault();
   if(item==null){log("沒有待處理碼可測試；請先掃描頻道。");return;}
   game=Desktop.Find("wwm");await Desktop.Focus(game);await Prepare(item.Code);
   log("輸入測試成功，未按確認、未送出兌換，紀錄仍為待兌換。可接著按「兌換待處理」。");
  }
  public async Task Prepare(string code) {
    var h=game;activeCode=code;
    Desktop.Check();await EnsureDialog(h);
    var screen=await ReadScreen(h);var title=screen.Title;
    if(!title.HasValue)throw new InvalidOperationException("兌換視窗已改變。");
    var b=Desktop.Bounds(h);
    int y=title.Value.Top+title.Value.Height/2+(int)(b.Height*.080);
    string[] modes={"Unicode 文字輸入","剪貼簿貼上","掃描碼按鍵"};
    for(int attempt=0;attempt<modes.Length;attempt++) {
     if(attempt>0){
      screen=await ReadScreen(h);
      if(!screen.Title.HasValue)throw new InvalidOperationException("輸入框已關閉，停止輸入。");
     }
     log("輸入方式："+modes[attempt]);
     Desktop.Click(h,b.Width/2,y);await Task.Delay(350);Desktop.Clear(h);
     if(attempt==0)Desktop.TypeUnicode(h,code);else if(attempt==1)Desktop.Type(h,code);else Desktop.TypeKeys(h,code);
     await Task.Delay(500);
     var inputShot=Desktop.Capture(h);
     var inputArea=InputArea(b.Width,b.Height,y);
     var cropped=Crop(inputShot,inputArea);
     bool verified=Desktop.ReadBackMatches(h,code);string verifiedBy=verified?"複製回讀":"OCR";string observed="";
     if(!verified){
      foreach(var variant in new[]{cropped,Ocr.Scale(cropped,2),Ocr.Scale(cropped,3)}){
       var text=Desktop.Text(await Ocr.Read(variant,"en-US"));observed+=text+"\n";
       if(InputMatches(text,code)){verified=true;break;}
      }
     }
     var diagnostics="方式："+modes[attempt]+"\n預期："+code+"\n點擊："+(b.Width/2)+","+y+"\nOCR 區域："+inputArea+"\n辨識：\n"+observed;
     File.WriteAllBytes(Path.Combine(store.Folder,"input-attempt-"+(attempt+1)+".png"),inputShot);
     File.WriteAllText(Path.Combine(store.Folder,"input-attempt-"+(attempt+1)+".txt"),diagnostics);
     if(verified) {
      screen=await ReadScreen(h);
      if(!screen.Title.HasValue||!screen.Find("確認").HasValue)throw new InvalidOperationException("找不到兌換確認介面，尚未送出。");
      dialogTitle=screen.Title.Value;log("已確認輸入內容："+code+"（"+verifiedBy+"／"+modes[attempt]+"）");return;
     }
     File.WriteAllBytes(Path.Combine(store.Folder,"last-input-check.png"),inputShot);
     File.WriteAllText(Path.Combine(store.Folder,"last-input-check.txt"),diagnostics);
     log("此方式未確認文字，"+(attempt+1<modes.Length?"嘗試下一種輸入方式。":"停止，尚未送出。"));
    }
    throw new InvalidOperationException("未能確認輸入內容「"+code+"」。已停止，尚未送出。");
  }
  public static bool InputMatches(string observed,string code){return String.Equals(Desktop.Compact(observed),code,StringComparison.OrdinalIgnoreCase);}
  static byte[] Crop(byte[] png,Rectangle area) {
   using(var stream=new MemoryStream(png))using(var source=new Bitmap(stream)){
    area=Rectangle.Intersect(area,new Rectangle(0,0,source.Width,source.Height));
    if(area.Width<1||area.Height<1)throw new InvalidOperationException("輸入框位置超出畫面。");
    using(var crop=source.Clone(area,source.PixelFormat))using(var output=new MemoryStream()){crop.Save(output,System.Drawing.Imaging.ImageFormat.Png);return output.ToArray();}
   }
  }
  public async Task<RedemptionResult> SubmitAndRead() {
    var h=game;
    string status="unknown",response="",evidence="";byte[] latest=null;
     var before=await ReadScreen(h);
     if(!before.Title.HasValue||!before.Find("確認").HasValue)throw new InvalidOperationException("送出前兌換介面已改變，停止。");
     // Leave the text editor before Space so the key is handled as the dialog shortcut.
     dialogTitle=before.Title.Value;Desktop.Click(h,dialogTitle.Left+dialogTitle.Width/2,dialogTitle.Top+dialogTitle.Height/2);await Task.Delay(150);
     log("按 Space 確認兌換。");Desktop.Key(h,0x20);
     // Capture independently of OCR: brief result toasts must not disappear between OCR calls.
     var frames=new ConcurrentQueue<byte[]>();var sampled=new List<byte[]>();var trace=new System.Text.StringBuilder();
     using(var stopCapture=new CancellationTokenSource()){
      Exception captureError=null;
      var capture=Task.Run(async ()=>{
       try{var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!stopCapture.IsCancellationRequested&&timer.ElapsedMilliseconds<6000){
         frames.Enqueue(Desktop.Capture(h));await Task.Delay(100);
        }
       }catch(Exception ex){captureError=ex;}
      });
      try{
       while(!capture.IsCompleted||!frames.IsEmpty){
        Desktop.Check();byte[] frame;if(!frames.TryDequeue(out frame)){await Task.Delay(25);continue;}
        sampled.Add(frame);latest=frame;
        var result=await InspectResultImage(frame);response=result.Response;status=result.Status;
        trace.AppendLine("Frame "+sampled.Count+" ["+status+"]\n"+response);
        if(status!="unknown")break;
       }
      }finally{stopCapture.Cancel();}
      await capture;if(captureError!=null&&status=="unknown")throw captureError;
     }
     if(status=="unknown"){
      string folder=Path.Combine(store.Folder,"evidence",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+activeCode+"-frames");Directory.CreateDirectory(folder);
      byte[] remaining;while(frames.TryDequeue(out remaining))sampled.Add(remaining);
      for(int i=0;i<sampled.Count;i++)File.WriteAllBytes(Path.Combine(folder,(i+1).ToString("D3")+".png"),sampled[i]);
      File.WriteAllText(Path.Combine(folder,"ocr.txt"),trace.ToString());
     }
     if(latest!=null) {Directory.CreateDirectory(Path.Combine(store.Folder,"evidence"));evidence="evidence/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+activeCode+".png";File.WriteAllBytes(Path.Combine(store.Folder,evidence),latest);}
     return new RedemptionResult{Status=status,Response=response,Evidence=evidence};
  }
  public static async Task<RedemptionResult> InspectResultImage(byte[] frame){
   string response=Desktop.Text(await Ocr.Read(frame));var status=Store.Classify(response);
   if(status=="unknown"){response+="\n"+Desktop.Text(await Ocr.Read(Ocr.Contrast(frame)));status=Store.Classify(response);}
   if(status=="unknown"){
    int width,height;using(var stream=new MemoryStream(frame))using(var bitmap=new Bitmap(stream)){width=bitmap.Width;height=bitmap.Height;}
    // Central game notifications; exclude the upper-right static redemption instructions.
    var center=Crop(frame,new Rectangle((int)(width*.2),(int)(height*.3),(int)(width*.6),(int)(height*.4)));
    response+="\n"+Desktop.Text(await Ocr.Read(Ocr.Scale(center,2)));status=Store.Classify(response);
   }
   return new RedemptionResult{Status=status,Response=response};
  }
  public async Task Finish() {
     var h=game;
     await Task.Delay(1500);int dismissCount=0,nextDismiss=0;byte[] latest=null;var trace=new System.Text.StringBuilder();
     for(int i=0;i<12;i++){
      latest=Desktop.Capture(h);var after=await ReadScreenshot(latest);
      trace.AppendLine("Check "+i+"; Settings="+after.Settings+"; Title="+after.Title+"; ExchangeRow="+after.ExchangeRow+"\n"+after.Text);
      if(after.Settings){log("已返回兌換設定頁，可繼續下一組。");return;}
      // Retry only when a fresh observation still identifies the redemption dialog.
      if(after.Title.HasValue&&dismissCount<3&&i>=nextDismiss){
       log("關閉兌換框，返回設定頁（"+(dismissCount+1)+"/3）。");
       Desktop.Key(h,0x1B);dismissCount++;nextDismiss=i+3;
      }
      await Task.Delay(300);
     }
     if(latest!=null)File.WriteAllBytes(Path.Combine(store.Folder,"last-return-check.png"),latest);
     File.WriteAllText(Path.Combine(store.Folder,"last-return-check.txt"),trace.ToString());
     throw new InvalidOperationException("此碼結果已保存，但未能返回兌換設定頁；停止下一筆，請確認遊戲畫面。");
  }
 }
}
