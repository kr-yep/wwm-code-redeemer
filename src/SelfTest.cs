using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace WwmRedeemer {
 public static class SelfTest {
  static void Assert(bool value,string message){if(!value)throw new Exception(message);}
  class FakeGame : IRedemptionClient {
   public List<string> Prepared=new List<string>();public int Submitted,Finished;
   public string Fail="";public Queue<string> Responses=new Queue<string>();
   public Action OnSubmit=()=>{};
   public Task Prepare(string code){Prepared.Add(code);if(Fail=="prepare")throw new Exception("input could not be verified");return Task.FromResult(0);}
   public Task<RedemptionResult> SubmitAndRead(){OnSubmit();Submitted++;if(Fail=="submit")throw new Exception("response lost after sending");return Task.FromResult(new RedemptionResult{Status=Responses.Dequeue(),Response="simulated game response"});}
   public Task Finish(){Finished++;if(Fail=="finish")throw new Exception("could not return to settings");return Task.FromResult(0);}
  }
  static Store QueueStore(string dir){var s=new Store(dir);s.Ingest(new SourceMessage{Id="100000000000000101",Text="QUEUE001 QUEUE002 QUEUE003 QUEUE004 QUEUE005"});return s;}
  static void RunQueue(Store s,FakeGame game,int limit=100){RedemptionWorkflow.Run(s,game,null,limit,x=>{},()=>{}).GetAwaiter().GetResult();}
  static SourceMessage MessageAt(DateTime time,string text){return new SourceMessage{Id=(((ulong)(new DateTimeOffset(time).ToUnixTimeMilliseconds()-1420070400000L))<<22).ToString(),Text=text};}
  public static void Run(string output,string history=null){
   string dir=Path.Combine(Path.GetTempPath(),"wwm-tests-"+Guid.NewGuid().ToString("N"));
   try{
    const string syntheticSource="https://discord.com/channels/100000000000000001/100000000000000002";
    SourceSettings.Configure(syntheticSource);
    Assert(SourceSettings.Normalize(syntheticSource+"/100000000000000003")==syntheticSource,"message URL normalizes to source thread");
    bool unsafeSource=false;try{SourceSettings.Normalize("https://example.com/channels/100000000000000001/100000000000000002");}catch(ArgumentException){unsafeSource=true;}Assert(unsafeSource,"non-Discord source rejected");
    Assert(Privileges.Blocked(false,true),"lower privilege cannot inject into elevated target");
    Assert(!Privileges.Blocked(true,true)&&!Privileges.Blocked(false,false)&&!Privileges.Blocked(true,false),"matching or higher privilege accepted");
    bool elevation=Privileges.CurrentElevated();
    Assert(Store.Extract("0921兌換碼: TESTCODE01 TESTCODE02 TESTCODE01").Count==2,"dedup");
    Assert(Store.Extract("兌換碼_2: samplea001 SAMPLE0916").SequenceEqual(new[]{"samplea001","SAMPLE0916"}),"formats");
    Assert(Store.Extract("這是一則一般聊天訊息").Count==0,"chat ignored");
    Assert(Store.Extract("新兌換碼SAMPLELETTERS @測試作者").Single()=="SAMPLELETTERS","April code touching Chinese label");
    Assert(Store.Extract("06/27兌換碼新增_3: abcdefghij").Single()=="abcdefghij","lowercase letter-only code");
    Assert(Store.Extract("SAMPLE0501").Single()=="SAMPLE0501","forwarded standalone code");
    Assert(Store.Extract("06/13下午新增： TESTCODE03 TESTCODE04 TESTCODE05 TESTCODE06").Count==4,"historical heading without code label");
    Assert(Store.Extract("06/19上午新增： SAMPLE0625 大禮包兌換碼").Single()=="SAMPLE0625","label after code");
    Assert(Store.Extract("TESTCODE07 (已編輯) 星期五, 六月 12, 2026 上午 09:33").Single()=="TESTCODE07","edited unlabelled post");
    Assert(Store.Extract("code : sampleb001 , samplec001").Count==2,"English prefix");
    Assert(Store.Extract(":BongoPleased: :emoji_56: @everyone").Count==0,"emoji and mentions excluded");
    Assert(Desktop.MessageBody("測試作者 原PO , , 2026/5/2 上午 11:23")=="","empty forwarded accessible body");
    Assert(Store.Extract(String.Join(" ",Enumerable.Range(1,30).Select(i=>"BATCH"+i))).Count==30,"all 30 codes in historical batch");
    Assert(Store.Extract("兌換碼: https://example.com/ABCDEF 20260921").Count==0,"urls and dates ignored");
    var s=new Store(dir);s.Ingest(new SourceMessage{Id="100000000000000101",Text="0921兌換碼: TESTCODE01 TESTCODE02"});
    s.Ingest(new SourceMessage{Id="100000000000000101",Text="0921兌換碼: TESTCODE01 TESTCODE02"});Assert(s.Events.Count==1,"idempotent ingestion");
    s.Begin("TESTCODE01");s.Result("TESTCODE01","success","兌換成功","");s.Begin("TESTCODE02");
    s=new Store(dir);Assert(s.Codes["TESTCODE02"].Status=="unknown","interrupted attempt not silently retried");Assert(s.Pending(null).Count==0,"completed/unknown skipped");
    s.Ingest(new SourceMessage{Id="100000000000000101",Text="0921兌換碼: TESTCODE01 TESTCODE02 NEWCODE777"});Assert(s.Pending(null).Single().Code=="NEWCODE777","edited old message backfill");
    s.Ingest(new SourceMessage{Id="100000000000000101",Text="0921兌換碼: TESTCODE01 TESTCODE02"});Assert(s.Pending(null).Count==0,"removed unattempted code excluded");
    s.Result("TESTCODE02","pending","user retry","");Assert(s.Pending(null).Count==1,"explicit retry");
    SourceSettings.Load(s);Assert(SourceSettings.Url==syntheticSource,"existing local history restores private source configuration");
    bool mixedSource=false;try{SourceSettings.Save(s,"https://discord.com/channels/100000000000000001/100000000000000004");}catch(InvalidOperationException){mixedSource=true;}Assert(mixedSource,"different source cannot mix with existing history");
    var freshSource=new Store(Path.Combine(dir,"fresh-source"));SourceSettings.Load(freshSource);Assert(SourceSettings.Url=="","new profile ships without source identifiers");
    SourceSettings.Save(freshSource,syntheticSource);SourceSettings.Load(freshSource);Assert(SourceSettings.Url==syntheticSource,"private source survives restart");
    var dated=new Store(Path.Combine(dir,"dates"));var now=new DateTime(2026,9,22,12,0,0,DateTimeKind.Local);var cutoff=now.AddDays(-15);
    dated.Ingest(MessageAt(cutoff.AddSeconds(-1),"OLDER001"));dated.Ingest(MessageAt(cutoff,"BOUNDARY001"));dated.Ingest(MessageAt(cutoff.AddSeconds(1),"RECENT001"));
    Assert(dated.Pending(cutoff).Select(c=>c.Code).SequenceEqual(new[]{"BOUNDARY001","RECENT001"}),"quick mode includes 15-day boundary but excludes one second older");
    Assert(dated.Pending(null).Count==3,"full mode includes older records");
    dated.Begin("RECENT001");dated.Result("RECENT001","success","verified","");Assert(dated.Pending(cutoff).Single().Code=="BOUNDARY001","quick mode skips completed records");
    var repost=MessageAt(now,"OLDER001");dated.Ingest(repost);Assert(dated.Pending(cutoff).Any(c=>c.Code=="OLDER001"),"recent repost makes old code eligible");
    dated.Ingest(MessageAt(now,"已移除文字碼"));Assert(!dated.Pending(cutoff).Any(c=>c.Code=="OLDER001"),"removed recent repost cannot make old code eligible");
    var checkpointStore=QueueStore(Path.Combine(dir,"checkpoint"));const string oldId="100000000000000101",newId="100000000000000102";
    checkpointStore.ScanStatus("掃描進行中；尚未確認完整。");Assert(checkpointStore.ScanCheckpointId==null,"partial initial scan has no checkpoint");
    checkpointStore.ScanStatus("完整連續掃描已核對；最新 ID "+oldId);Assert(checkpointStore.ScanCheckpointId==oldId,"legacy complete scan migrates checkpoint");
    checkpointStore.Ingest(new SourceMessage{Id=newId,Text="NEWSCAN001"});checkpointStore.ScanStatus("增量掃描進行中；尚未確認完整。");
    checkpointStore=new Store(checkpointStore.Folder);Assert(checkpointStore.ScanCheckpointId==oldId,"interrupted incremental scan cannot advance checkpoint");
    checkpointStore.CompleteScan("增量文字掃描已核對",newId);Assert(new Store(checkpointStore.Folder).ScanCheckpointId==newId,"completed incremental checkpoint survives restart");
    var incremental=new ScanCoverage("old","new");incremental.Add(new[]{"new","middle"});Assert(!incremental.Complete,"newest page alone cannot skip gap");incremental.Add(new[]{"middle","old"});Assert(incremental.Complete,"overlapping pages connect to old checkpoint");
    var gap=new ScanCoverage("old","new");gap.Add(new[]{"new","middle"});bool gapStopped=false;try{gap.Add(new[]{"old"});}catch(InvalidOperationException){gapStopped=true;}Assert(gapStopped&&!gap.Complete,"non-overlapping page cannot complete scan");
    var unchanged=new ScanCoverage("same","same");unchanged.Add(new[]{"same"});Assert(unchanged.Complete,"no-new-messages scan completes immediately");
    var deleted=new ScanCoverage("deleted","new");deleted.Add(new[]{"new","middle"});deleted.Add(new[]{"middle",Store.Channel});Assert(deleted.Complete,"deleted checkpoint can recover only after reaching thread start");
    var firstScan=new ScanCoverage(Store.Channel,"new");firstScan.Add(new[]{Store.Channel,"middle"});Assert(!firstScan.Complete,"first scan must reach latest");firstScan.Add(new[]{"middle","new"});Assert(firstScan.Complete,"first scan covers both endpoints");
    Assert(Store.Classify("此兌換碼已領取")=="already","already");Assert(Store.Classify("兌 換 成 功")=="success","ocr whitespace");
    Assert(Store.Classify("兌換失敗，請稍後再試")=="unknown","network failure retained");Assert(Store.Classify("兌換碼無效")=="invalid","invalid");
    Assert(Store.Classify("輸入兌換碼兌換獎勵，兌換成功後可在郵件中查收")=="unknown","settings instructions are not a successful redemption");
    Assert(Store.Classify("兌換成功後可在郵件中查收\n此兌換碼已過期")=="expired","result wins over background instructions");
    Assert(Store.Classify("兌 换 成 功 後 可 在 件 中 查 收")=="unknown","mixed OCR variants of instructions are not success");
    Assert(Store.Classify("兌 换 成 功")=="success","mixed OCR variants of real success");
    foreach(var successText in new[]{"兌換碼使用成功，請少俠在郵件中查收","兌 換 碼 使 用 成 功 ， 請 少 俠 在 郵 件 中 查 收","兑换码使用成功，请少侠在邮件中查收","兌换碼使用\n成功，請少俠在郵件中查收"})Assert(Store.Classify(successText)=="success","observed game success: "+successText);
    Assert(Store.Classify("兌換成功後可在郵件中查收\n兌換碼使用成功，請少俠在郵件中查收")=="success","actual success alongside static instructions");
    Assert(Store.Classify("兌換碼使用成功後可在郵件中查收")=="unknown","conditional success instructions are not results");
    Assert(Store.Classify("請少俠在郵件中查收")=="unknown","mail instructions alone cannot prove redemption");
    foreach(var limitText in new[]{"兌換碼已達上限","兌 換 碼 已 達 上 限","兑换码使用次数已达到上限","此兌換碼的兌換次數已達上限"})Assert(Store.Classify(limitText)=="exhausted","code quota: "+limitText);
    Assert(Store.Classify("帳戶資訊\n兌換碼已達上限\n兌換成功後可在郵件中查收")=="exhausted","settings account heading cannot mask code quota");
    foreach(var limitText in new[]{"今日兌換碼已達上限","帳號兌換次數已達上限","兌換碼已達上限，請稍後再試","上限"})Assert(Store.Classify(limitText)=="unknown","ambiguous or account quota stops: "+limitText);
    Assert(Runner.InputMatches("AB C1 23","ABC123"),"OCR whitespace accepted");
    Assert(!Runner.InputMatches("ABC1237","ABC123"),"extra characters cannot pass input verification");
    Assert(!Runner.InputMatches("ABC12","ABC123"),"truncated input cannot pass verification");
    Assert(Runner.AccountMatches("ID: 1234 567890","1234567890"),"spaced OCR account ID");
    Assert(!Runner.AccountMatches("ID: 11234567890","1234567890"),"different account ID cannot pass substring match");
    var batch=QueueStore(Path.Combine(dir,"batch"));var game=new FakeGame{Responses=new Queue<string>(new[]{"success","already","expired","invalid"})};
    game.OnSubmit=()=>Assert(batch.Codes[game.Prepared.Last()].Status=="inflight" && File.ReadAllText(Path.Combine(batch.Folder,"journal.jsonl")).Contains("\"begin\""),"begin durably written before send");
    RunQueue(batch,game,4);Assert(game.Submitted==4 && game.Finished==4 && batch.Pending(null).Single().Code=="QUEUE005","terminal responses advance sequentially and respect batch limit");
    batch=new Store(batch.Folder);var resumed=new FakeGame{Responses=new Queue<string>(new[]{"success"})};RunQueue(batch,resumed);
    Assert(resumed.Prepared.SequenceEqual(new[]{"QUEUE005"}),"restart submits only the remaining code");
    var quota=QueueStore(Path.Combine(dir,"quota"));var quotaGame=new FakeGame{Responses=new Queue<string>(new[]{"exhausted","expired"})};RunQueue(quota,quotaGame,2);
    var quotaRestored=new Store(quota.Folder);
    Assert(quotaGame.Submitted==2&&quotaGame.Finished==2&&quotaRestored.Codes["QUEUE001"].Status=="exhausted"&&quotaRestored.Pending(null).Count==3,"code quota persists and advances without resubmission");
    var uncertain=QueueStore(Path.Combine(dir,"unknown"));var unknown=new FakeGame{Responses=new Queue<string>(new[]{"unknown"})};
    bool stopped=false;try{RunQueue(uncertain,unknown);}catch(InvalidOperationException){stopped=true;}
    Assert(stopped&&unknown.Submitted==1&&unknown.Finished==0&&uncertain.Codes["QUEUE001"].Status=="unknown"&&uncertain.Pending(null).Count==4,"unknown result stops without sending next code");
    foreach(var phase in new[]{"prepare","submit"}){
     var failing=QueueStore(Path.Combine(dir,phase));var client=new FakeGame{Fail=phase};try{RunQueue(failing,client);}catch(Exception){}
     var restored=new Store(failing.Folder);
     Assert(restored.Codes["QUEUE001"].Status==(phase=="prepare"?"pending":"unknown"),"interrupted "+phase+" retains correct restart status");
     Assert(client.Prepared.Count==1&&client.Finished==0,"failure never continues to next code");
    }
    var finishing=QueueStore(Path.Combine(dir,"finish"));var finishClient=new FakeGame{Fail="finish",Responses=new Queue<string>(new[]{"success"})};
    try{RunQueue(finishing,finishClient);}catch(Exception){}
    Assert(finishClient.Submitted==1&&finishing.Codes["QUEUE001"].Status=="success"&&finishing.Pending(null).Count==4,"return-to-settings failure preserves known outcome and stops next input");
    var afterFinishFailure=new FakeGame{Responses=new Queue<string>(new[]{"already"})};RunQueue(new Store(finishing.Folder),afterFinishFailure,1);
    Assert(afterFinishFailure.Prepared.Single()=="QUEUE002","restart after finish failure never resubmits completed code");
    string audit="";
    if(history!=null){
     var auditDir=Path.Combine(dir,"history");Directory.CreateDirectory(auditDir);File.Copy(history,Path.Combine(auditDir,"journal.jsonl"));
     var saved=new Store(auditDir);audit="History replay: "+saved.Messages.Count+" messages, "+saved.Codes.Count+" text codes, "+saved.Pending(null).Count+" pending; "+saved.Messages.Values.Min(m=>m.Published)+" ~ "+saved.Messages.Values.Max(m=>m.Published)+"; scan checkpoint: "+saved.ScanCheckpointId+".\n";
    }
    File.WriteAllText(output,"PASS: text extraction, dedup, replay, message edits, queue sequencing, batch limit, durable begin, interrupted input/send, unknown stops, resume, exact input verification.\n"+audit);
   }catch(Exception ex){File.WriteAllText(output,"FAIL: "+ex);Environment.ExitCode=1;}
  }
 }
}
