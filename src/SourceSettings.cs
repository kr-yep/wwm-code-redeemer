using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WwmRedeemer {
 public static class SourceSettings {
  public static string Guild {get;private set;}
  public static string Channel {get;private set;}
  public static string Url {get{return String.IsNullOrEmpty(Channel)?"":"https://discord.com/channels/"+Guild+"/"+Channel;}}
  public static string Normalize(string value){
   var match=Regex.Match((value??"").Trim(),@"^https://discord\.com/channels/(\d{17,20})/(\d{17,20})(?:/\d{17,20})?/?$",RegexOptions.IgnoreCase);
   if(!match.Success)throw new ArgumentException("請貼上 Discord 伺服器內的討論串連結（https://discord.com/channels/…/…）。");
   return "https://discord.com/channels/"+match.Groups[1].Value+"/"+match.Groups[2].Value;
  }
  public static void Configure(string value){var parts=Normalize(value).Split('/');Guild=parts[4];Channel=parts[5];}
  public static void Load(Store store){
   Guild=null;Channel=null;
   var sources=store.Messages.Values.Where(m=>!String.IsNullOrEmpty(m.Url)).Select(m=>Normalize(m.Url)).Distinct().ToList();
   if(sources.Count>1)throw new InvalidDataException("此角色紀錄包含不同來源，請先核對本機資料。");
   string path=Path.Combine(store.Folder,"source.txt");
   if(sources.Count==1){Configure(sources[0]);File.WriteAllText(path,Url);}
   else if(File.Exists(path))Configure(File.ReadAllText(path));
  }
  public static void Save(Store store,string value){
   string normalized=Normalize(value);
   if(store.Messages.Values.Any(m=>!String.IsNullOrEmpty(m.Url)&&Normalize(m.Url)!=normalized))throw new InvalidOperationException("已有其他來源的掃描紀錄，不能混入新的討論串。");
   Configure(normalized);File.WriteAllText(Path.Combine(store.Folder,"source.txt"),Url);
  }
 }
}
