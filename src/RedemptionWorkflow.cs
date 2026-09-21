using System;
using System.Linq;
using System.Threading.Tasks;
namespace WwmRedeemer {
 public class RedemptionResult {public string Status,Response,Evidence;}
 public interface IRedemptionClient {
  Task Prepare(string code);
  Task<RedemptionResult> SubmitAndRead();
  Task Finish();
 }
 public static class RedemptionWorkflow {
  // The same queue is used by the desktop client and offline simulated-game tests.
  public static async Task Run(Store store,IRedemptionClient client,DateTime? since,int limit,Action<string> log,Action check) {
   var queue=store.Pending(since).Take(limit).ToList();
   if(queue.Count==0){log("此範圍沒有待兌換碼。");return;}
   foreach(var item in queue) {
    check();await client.Prepare(item.Code);check();
    store.Begin(item.Code);log("正在兌換 "+item.Code);
    try {
     var result=await client.SubmitAndRead();
     if(result==null || !new[]{"success","already","expired","invalid","exhausted","unknown"}.Contains(result.Status))throw new InvalidOperationException("遊戲回應格式無法確認。");
     store.Result(item.Code,result.Status,result.Response??"",result.Evidence??"");
     log(item.Code+"："+Store.Label(result.Status));
     if(result.Status=="unknown")throw new InvalidOperationException("無法確認遊戲回應，已保存紀錄。請檢視「待確認」項目後再繼續。");
     check();await client.Finish();
    }catch(Exception ex){
     if(store.Codes[item.Code].Status=="inflight")store.Result(item.Code,"unknown",ex.Message,"");
     throw;
    }
   }
   log("本批次完成："+queue.Count+" 組。下次會接續剩餘待兌換碼。");
  }
 }
}
