using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WwmRedeemer {
 public class MainForm:Form {
  readonly string root=AppDomain.CurrentDomain.BaseDirectory;
  Store store;bool running;string activeAccount="";
  TextBox account=new TextBox{Width=155};
  Button scan=new Button{Text="掃描頻道",AutoSize=true},quick=new Button{Text="快速兌換",AutoSize=true},full=new Button{Text="完整兌換",AutoSize=true},stop=new Button{Text="停止",AutoSize=true,Enabled=false};
  Label status=new Label{AutoSize=true,MaximumSize=new Size(1050,0)};
  DataGridView grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false};
  TextBox logBox=new TextBox{Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,ReadOnly=true};
  ContextMenuStrip details=new ContextMenuStrip();
  public MainForm() {
   Text="燕雲兌換助手 v"+Program.Version+"（文字碼版）";Size=new Size(1130,800);MinimumSize=new Size(980,650);Font=new Font("Microsoft JhengHei UI",10);StartPosition=FormStartPosition.CenterScreen;
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(14)};
   layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,30));Controls.Add(layout);
   layout.Controls.Add(new Label{Text="先掃描頻道，再選擇快速兌換（最近 15 天）或完整兌換（全部待處理）。\n兌換時請將遊戲停在「設定 → 其他 → 兌換碼」。F10 隨時停止；進度自動保存。",AutoSize=true,Padding=new Padding(0,0,0,10)});
   var profile=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};profile.Controls.Add(new Label{Text="遊戲角色 ID",AutoSize=true,Padding=new Padding(0,5,0,0)});profile.Controls.Add(account);profile.Controls.Add(new Label{Text="輸入後自動載入進度",AutoSize=true,Padding=new Padding(8,5,0,0)});layout.Controls.Add(profile);
   var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};actions.Controls.AddRange(new Control[]{scan,quick,full,stop});layout.Controls.Add(actions);
   layout.Controls.Add(status);layout.Controls.Add(grid);layout.Controls.Add(logBox);
   grid.Columns.Add("code","兌換碼");grid.Columns.Add("state","狀態");grid.Columns.Add("published","來源日期");grid.Columns.Add("updated","最後處理");grid.Columns.Add("result","遊戲回應摘要");
   grid.Columns[0].FillWeight=95;grid.Columns[1].FillWeight=55;grid.Columns[2].FillWeight=100;grid.Columns[3].FillWeight=100;grid.Columns[4].FillWeight=230;
   details.Items.Add("查看結果截圖",null,(s,e)=>{var c=Selected();if(c!=null&&!String.IsNullOrEmpty(c.Evidence))System.Diagnostics.Process.Start(Path.Combine(store.Folder,c.Evidence));else MessageBox.Show("此項目尚無結果截圖。");});
   details.Items.Add("查看來源訊息",null,(s,e)=>{var c=Selected();if(c!=null)System.Diagnostics.Process.Start(store.Messages[c.Sources[0]].Url);});
   details.Items.Add(new ToolStripSeparator());
   foreach(var state in new[]{"success","already","expired","invalid","exhausted","pending"}){
    string target=state;string label=state=="pending"?"重新嘗試此碼":"手動確認："+Store.Label(state);
    details.Items.Add(label,null,(s,e)=>Resolve(target,label));
   }
   grid.ContextMenuStrip=details;details.Opening+=(s,e)=>{e.Cancel=running||Selected()==null;};
   grid.CellMouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right&&e.RowIndex>=0){grid.ClearSelection();grid.Rows[e.RowIndex].Selected=true;grid.CurrentCell=grid.Rows[e.RowIndex].Cells[0];}};
   account.Leave+=(s,e)=>{if(!running&&account.Text.Trim()!=activeAccount)LoadProfile();};
   account.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){LoadProfile();e.SuppressKeyPress=true;}};
   scan.Click+=async(s,e)=>await Start(true,false);quick.Click+=async(s,e)=>await Start(false,true);full.Click+=async(s,e)=>await Start(false,false);
   stop.Click+=(s,e)=>{Desktop.Stop=true;Log("正在停止；會保存已送出項目的狀態。");};
   FormClosing+=(s,e)=>{if(running){Desktop.Stop=true;e.Cancel=true;Log("正在停止，保存紀錄後可關閉。");}};
   var cfg=Path.Combine(root,"data","last-profile.txt");if(File.Exists(cfg))account.Text=File.ReadAllText(cfg).Trim();
   LoadProfile();Log("文字碼版 v"+Program.Version+"；權限："+(Privileges.CurrentElevated()?"系統管理員":"一般")+"；程式："+Application.ExecutablePath);
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);RegisterHotKey(Handle,10,0x4000,0x79);}
  protected override void OnHandleDestroyed(EventArgs e){UnregisterHotKey(Handle,10);base.OnHandleDestroyed(e);}
  protected override void WndProc(ref Message m){if(m.Msg==0x312&&m.WParam.ToInt32()==10){Desktop.Stop=true;Log("F10：已要求停止。");}base.WndProc(ref m);}
  CodeItem Selected(){if(store==null||grid.SelectedRows.Count==0)return null;var key=Convert.ToString(grid.SelectedRows[0].Cells[0].Value);return store.Codes.ContainsKey(key)?store.Codes[key]:null;}
  void LoadProfile(){
   if(running)return;string profile=account.Text.Trim();
   if(!System.Text.RegularExpressions.Regex.IsMatch(profile,@"^\d{5,20}$")){store=null;activeAccount="";grid.Rows.Clear();status.Text="請填入遊戲左下角的角色 ID。";return;}
   try {
    store=new Store(Path.Combine(root,"data","profiles",profile));SourceSettings.Load(store);activeAccount=profile;account.Text=profile;Directory.CreateDirectory(Path.Combine(root,"data"));File.WriteAllText(Path.Combine(root,"data","last-profile.txt"),profile);RefreshRows();
   }catch(Exception ex){store=null;activeAccount="";grid.Rows.Clear();status.Text="讀取進度失敗："+ex.Message;}
  }
  void Resolve(string state,string action){var c=Selected();if(c==null||running)return;if(MessageBox.Show(c.Code+"\n"+action+"？\n此操作會更改下次是否自動嘗試此碼。","更新此碼紀錄",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;store.Result(c.Code,state,"使用者手動操作："+action,c.Evidence);RefreshRows();}
  void RefreshRows(){
   grid.Rows.Clear();if(store==null)return;
   foreach(var c in store.Codes.Values.OrderBy(c=>c.Sources.Min()))grid.Rows.Add(c.Code,Store.Label(c.Status),store.Messages[c.Sources[0]].Published,c.Updated==""?"":DateTime.Parse(c.Updated).ToLocalTime().ToString("MM-dd HH:mm:ss"),c.Response.Replace("\r","").Replace("\n"," / "));
   status.Text="角色 "+activeAccount+" ｜ "+store.Codes.Count+" 組碼 ｜ "+String.Join("、",store.Codes.Values.GroupBy(c=>c.Status).Select(g=>Store.Label(g.Key)+" "+g.Count()))+"\n"+store.LastScan;
  }
  void Log(string text){if(InvokeRequired){BeginInvoke(new Action<string>(Log),text);return;}var line=DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine;logBox.AppendText(line);Directory.CreateDirectory(Path.Combine(root,"data"));File.AppendAllText(Path.Combine(root,"data","activity.log"),line);}
  async Task Start(bool scanOnly,bool recentOnly){
   if(running)return;if(store==null||account.Text.Trim()!=activeAccount)LoadProfile();if(store==null)return;
   if(scanOnly&&String.IsNullOrEmpty(SourceSettings.Channel)&&!AskSource())return;
   DateTime? date=recentOnly?(DateTime?)DateTime.Now.AddDays(-15):null;
   if(!scanOnly&&store.Codes.Count==0){Log("尚無掃描紀錄，請先按「掃描頻道」。");return;}
   if(!scanOnly&&store.Pending(date).Count==0){Log(recentOnly?"最近 15 天沒有待兌換碼；有新訊息時請先掃描頻道。":"沒有待兌換碼；有新訊息時請先掃描頻道。");return;}
   running=true;Desktop.Stop=false;foreach(var c in new Control[]{account,scan,quick,full})c.Enabled=false;stop.Enabled=true;
   string profile=activeAccount;
   Log(scanOnly?(String.IsNullOrEmpty(store.ScanCheckpointId)?"掃描頻道：首次核對全部文字訊息。":"掃描頻道：從上次完成的進度補掃新訊息。"):recentOnly?"快速兌換：使用已掃描紀錄，範圍自 "+date.Value.ToString("yyyy-MM-dd HH:mm:ss")+" 起。":"完整兌換：使用已掃描紀錄，處理全部待兌換碼。");
   Log("三秒後開始；F10 隨時停止。");await Task.Delay(3000);
   try {
    await Task.Run(async()=>{
     Desktop.Check();var runner=new Runner(store,Log);
     if(scanOnly)await runner.Scan();
     else{await runner.VerifyAccount(profile);await runner.Redeem(date,Int32.MaxValue);}
    });
   }catch(Exception ex){if(scanOnly)store.ScanStatus(DateTime.Now.ToString("yyyy-MM-dd HH:mm")+" 掃描未完成；上次完整進度已保留。"+ex.Message);Log(ex.GetType().Name+": "+ex.Message);File.WriteAllText(Path.Combine(root,"data","last-error.txt"),ex.ToString());}
   finally{running=false;foreach(var c in new Control[]{account,scan,quick,full})c.Enabled=true;stop.Enabled=false;RefreshRows();Log("已停止操作，可使用滑鼠鍵盤。紀錄已保存在本機。");}
  }
  bool AskSource(){
   using(var dialog=new Form{Text="設定來源討論串",ClientSize=new Size(640,145),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font}){
    var label=new Label{Text="貼上你有閱讀權限的 Discord 兌換碼討論串連結；只保存在本機。",AutoSize=true,Left=12,Top=15};
    var input=new TextBox{Left=12,Top=48,Width=610};var ok=new Button{Text="保存",Left=440,Top=95,Width=85};var cancel=new Button{Text="取消",Left=537,Top=95,Width=85,DialogResult=DialogResult.Cancel};
    ok.Click+=(s,e)=>{try{SourceSettings.Save(store,input.Text);dialog.DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"來源連結無效");}};
    dialog.Controls.AddRange(new Control[]{label,input,ok,cancel});dialog.AcceptButton=ok;dialog.CancelButton=cancel;
    return dialog.ShowDialog(this)==DialogResult.OK;
   }
  }
 }
}
