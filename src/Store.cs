using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WwmRedeemer {
 public class SourceMessage { public string Id; public string Text; public string Url; public string Published; }
 public class CodeItem {
  public string Code; public string Status = "pending"; public string Response = "";
  public string Updated = ""; public string Evidence = ""; public List<string> Sources = new List<string>();
 }
 public class JournalEvent {
  public string Type; public string At; public SourceMessage Message;
  public string Code; public string Status; public string Response; public string Evidence;
  public string Checkpoint;
 }
 public class Store {
  public static string Guild {get{return SourceSettings.Guild;}}
  public static string Channel {get{return SourceSettings.Channel;}}
  public static string ChannelUrl {get{return SourceSettings.Url;}}
  public readonly string Folder;
  public Dictionary<string,SourceMessage> Messages = new Dictionary<string,SourceMessage>();
  public Dictionary<string,CodeItem> Codes = new Dictionary<string,CodeItem>(StringComparer.OrdinalIgnoreCase);
  public List<JournalEvent> Events = new List<JournalEvent>();
  public string LastScan = "尚未掃描";
  public string ScanCheckpointId;
  static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
  public Store(string folder) {
   Folder = folder; Directory.CreateDirectory(folder);
   var path = Path.Combine(folder,"journal.jsonl");
   if (File.Exists(path)) {
    var text = File.ReadAllText(path, Encoding.UTF8);
    if (text.Length > 0 && !text.EndsWith("\n")) throw new InvalidDataException("進度檔最後一筆不完整；已停止。請保留 data 資料夾以便修復，勿刪除紀錄。");
    foreach (var line in text.Split('\n').Where(x=>x.Trim().Length>0)) Apply(Json.Deserialize<JournalEvent>(line));
   }
   foreach (var c in Codes.Values.Where(c=>c.Status=="inflight").ToList()) Result(c.Code,"unknown","上次在送出前後中斷，結果未確認。","");
  }
  void Append(JournalEvent e) {
   e.At = DateTime.UtcNow.ToString("o");
   byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Serialize(e)+"\n");
   using (var f=new FileStream(Path.Combine(Folder,"journal.jsonl"),FileMode.Append,FileAccess.Write,FileShare.Read)) { f.Write(bytes,0,bytes.Length); f.Flush(true); }
   Apply(e);
  }
  void Apply(JournalEvent e) {
   Events.Add(e);
   if(e.Type=="message") {
    Messages[e.Message.Id] = e.Message;
    foreach(var code in Extract(e.Message.Text)) {
     CodeItem item;
     if(!Codes.TryGetValue(code,out item)) { item=new CodeItem { Code=code }; Codes.Add(code,item); }
     if(!item.Sources.Contains(e.Message.Id)) item.Sources.Add(e.Message.Id);
    }
   } else if(e.Type=="result" || e.Type=="begin") {
    if(!Codes.ContainsKey(e.Code)) throw new InvalidDataException("紀錄引用了不存在的兌換碼。");
    var item=Codes[e.Code]; item.Status=e.Status; item.Response=e.Response; item.Evidence=e.Evidence; item.Updated=e.At;
   } else if(e.Type=="scan") {
    LastScan=e.Response;
    string checkpoint=e.Checkpoint;
    // Migrate completed v0.4 scans; partial scans must never advance the checkpoint.
    if(String.IsNullOrEmpty(checkpoint)&&Regex.IsMatch(e.Response??"","完整(?:文字|連續)掃描已核對")){
     var match=Regex.Match(e.Response,@"最新 ID (\d{17,20})");if(match.Success)checkpoint=match.Groups[1].Value;
    }
    if(!String.IsNullOrEmpty(checkpoint)&&Messages.ContainsKey(checkpoint))ScanCheckpointId=checkpoint;
   }
  }
  public bool Ingest(SourceMessage m) {
   if(String.IsNullOrEmpty(Channel))throw new InvalidOperationException("尚未設定來源討論串。");
   if(!Regex.IsMatch(m.Id??"",@"^\d{17,20}$")) throw new InvalidDataException("訊息 ID 格式錯誤");
   m.Url=ChannelUrl+"/"+m.Id;
   m.Published=DateTimeOffset.FromUnixTimeMilliseconds((long)(UInt64.Parse(m.Id)>>22)+1420070400000L).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
   SourceMessage old;
   if(Messages.TryGetValue(m.Id,out old) && old.Text==m.Text) return false;
   Append(new JournalEvent { Type="message",Message=m }); return true;
  }
  public static List<string> Extract(string text) {
   // Historical posts also use dates, "新增", English "code", or put the label AFTER a code.
   // Extract from the entire body; remove non-content tokens before finding code-shaped text.
   var body=Regex.Replace(text??"",@"https?://\S+|:[A-Za-z0-9_]+:|@[A-Za-z0-9_]+", " ");
   return Regex.Matches(body,@"(?<![A-Za-z0-9_])[A-Za-z0-9]{6,32}(?![A-Za-z0-9_])").Cast<Match>()
    .Select(m=>m.Value).Where(s=>s.Any(Char.IsLetter)&&(s.Any(Char.IsDigit)||s==s.ToUpperInvariant()||s.Length==10)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  }
  public List<CodeItem> Pending(DateTime? since) {
   return Codes.Values.Where(c=>c.Status=="pending" && c.Sources.Any(id=>Extract(Messages[id].Text).Contains(c.Code,StringComparer.OrdinalIgnoreCase) &&
    (!since.HasValue || DateTime.Parse(Messages[id].Published)>=since.Value)))
    .OrderBy(c=>c.Sources.Min()).ToList();
  }
  public void Begin(string code) {
   if(!Codes.ContainsKey(code) || Codes[code].Status!="pending") throw new InvalidOperationException("此碼不是待兌換狀態。");
   Append(new JournalEvent {Type="begin",Code=code,Status="inflight",Response="已開始；尚未確認結果"});
  }
  public void Result(string code,string status,string response,string evidence) {
   if(!new[]{"success","already","expired","invalid","exhausted","unknown","pending"}.Contains(status)) throw new ArgumentException("未知狀態");
   Append(new JournalEvent {Type="result",Code=code,Status=status,Response=response,Evidence=evidence});
  }
  public void ScanStatus(string text) { Append(new JournalEvent{Type="scan",Response=text}); }
  public void CompleteScan(string text,string checkpoint) {
   if(!Messages.ContainsKey(checkpoint))throw new InvalidOperationException("掃描終點尚未保存。");
   Append(new JournalEvent{Type="scan",Response=text,Checkpoint=checkpoint});
  }
  public static string Label(string status) {
   switch(status) {case "pending":return "待兌換";case "inflight":return "處理中";case "success":return "成功";case "already":return "已領取";case "expired":return "已過期";case "invalid":return "無效";case "exhausted":return "已達上限";default:return "待確認";}
  }
  public static string Classify(string text) {
   // The settings page says "兌換成功後可在郵件中查收"; that is an instruction, not a result.
   var resultLines=(text??"").Split('\n').Where(line=>!Regex.IsMatch(Regex.Replace(line,@"\s+",""),"[兌兑][換换](?:[碼码]使用)?成功[後后]"));
   var s=Regex.Replace(String.Join("\n",resultLines),@"\s+","");
   // Specific negative results precede success. Unknown responses never advance the queue.
   // Only a code-specific quota is terminal. Account/daily/rate limits must stop the batch.
   var quotaLines=resultLines.Select(line=>Regex.Replace(line,@"\s+","")).Where(line=>line.Contains("上限")).ToList();
   if(quotaLines.Any(line=>Regex.IsMatch(line,"今日|每日|每天|本日|本週|本周|本月|[帳账][號号戶户]|角色|[頻频]繁|稍[後后]再[試试]")))return "unknown";
   if(quotaLines.Any(line=>Regex.IsMatch(line,"[兌兑][換换][碼码](?:的)?(?:[兌兑][換换]|使用|[領领]取)?(?:次[數数]|[數数]量|名[額额])?(?:已)?(?:[達达]到?|超[過过])上限")))return "exhausted";
   if(Regex.IsMatch(s,"已兌換|已兑换|已領取|已领取|已使用|重複兌換|重复兑换|alreadyredeemed",RegexOptions.IgnoreCase)) return "already";
   if(Regex.IsMatch(s,"已過期|已过期|已失效|超過有效期|超过有效期|expired",RegexOptions.IgnoreCase)) return "expired";
   if(Regex.IsMatch(s,"兌換碼無效|兑换码无效|兌換碼不存在|兑换码不存在|兌換碼錯誤|兑换码错误|invalidcode",RegexOptions.IgnoreCase)) return "invalid";
   if(Regex.IsMatch(s,"[兌兑][換换](?:[碼码]使用)?成功|領取成功|领取成功|獎勵已發放|奖励已发放|兌換獎勵已發送|redeemsuccessful",RegexOptions.IgnoreCase)) return "success";
   return "unknown";
  }
 }
}
