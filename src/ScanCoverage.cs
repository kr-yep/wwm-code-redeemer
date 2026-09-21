using System;
using System.Collections.Generic;
using System.Linq;
namespace WwmRedeemer {
 public sealed class ScanCoverage {
  readonly string anchor,target;
  HashSet<string> previous=new HashSet<string>();
  readonly HashSet<string> seen=new HashSet<string>();
  public ScanCoverage(string anchor,string target){this.anchor=anchor;this.target=target;}
  public bool Complete {get{return seen.Contains(target)&&(seen.Contains(anchor)||seen.Contains(Store.Channel));}}
  public int Count {get{return seen.Count;}}
  public IEnumerable<string> Seen {get{return seen;}}
  public void Add(IEnumerable<string> ids){
   var page=new HashSet<string>(ids);
   if(page.Count==0)throw new InvalidOperationException("未讀到訊息，掃描進度不會前移。");
   if(previous.Count>0&&!page.Overlaps(previous))throw new InvalidOperationException("前後頁沒有重疊訊息，可能漏頁。已保留資料，掃描進度不會前移，請重試。");
   seen.UnionWith(page);previous=page;
  }
 }
}
