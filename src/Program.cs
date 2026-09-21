using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
[assembly:System.Reflection.AssemblyTitle("燕雲兌換助手")]
[assembly:System.Reflection.AssemblyVersion("0.5.1.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.5.1.0")]
namespace WwmRedeemer {
 public static class Program {
  public const string Version="0.5.1";
  [STAThread] public static void Main(string[] args) {
   if(args.Length==2 && args[0]=="--restart-from"){
    int parent;if(Int32.TryParse(args[1],out parent))try{using(var process=System.Diagnostics.Process.GetProcessById(parent)){if(!process.WaitForExit(15000)){MessageBox.Show("原助手尚未關閉，請稍後重新開啟。");return;}}}catch(ArgumentException){}
   }
   if(args.Length>0 && args[0]=="--self-test") { SelfTest.Run(args.Length>1?args[1]:"self-test.txt",args.Length>2?args[2]:null);return; }
   if(args.Length==3&&args[0]=="--screen-image-test"){
    try{File.WriteAllText(args[2],Runner.InspectScreenImage(File.ReadAllBytes(args[1])).GetAwaiter().GetResult());}
    catch(Exception ex){File.WriteAllText(args[2],ex.ToString());Environment.ExitCode=1;}return;
   }
   if(args.Length==3&&args[0]=="--result-image-test"){
    try{var result=Runner.InspectResultImage(File.ReadAllBytes(args[1])).GetAwaiter().GetResult();File.WriteAllText(args[2],result.Status+"\n"+result.Response);}
    catch(Exception ex){File.WriteAllText(args[2],ex.ToString());Environment.ExitCode=1;}return;
   }
   if(args.Length==4&&args[0]=="--account-image-test"){
    try{var report=Runner.InspectAccountImage(File.ReadAllBytes(args[1]),args[2]).GetAwaiter().GetResult();File.WriteAllText(args[3],report);if(!report.StartsWith("MATCH\n"))Environment.ExitCode=1;}
    catch(Exception ex){File.WriteAllText(args[3],ex.ToString());Environment.ExitCode=1;}return;
   }
   if(args.Length==4&&args[0]=="--input-image-test"){
    try{var report=Runner.InspectInputImage(File.ReadAllBytes(args[1]),args[2]).GetAwaiter().GetResult();File.WriteAllText(args[3],report);if(!report.StartsWith("MATCH\n"))Environment.ExitCode=1;}
    catch(Exception ex){File.WriteAllText(args[3],ex.ToString());Environment.ExitCode=1;}return;
   }
   if(args.Length>1 && args[0]=="--ocr-test") {
    try {var bytes=File.ReadAllBytes(args[1]);var r=Ocr.Read(bytes).GetAwaiter().GetResult();var contrast=Ocr.Read(Ocr.Contrast(bytes)).GetAwaiter().GetResult();File.WriteAllText(args[1]+".txt","ORIGINAL\n"+Desktop.Text(r)+"\nCONTRAST\n"+Desktop.Text(contrast));}
    catch(Exception ex) {File.WriteAllText(args[1]+".txt",ex.ToString());Environment.ExitCode=1;}return;
   }
   bool created;using(var mutex=new Mutex(true,"Local\\WwmCodeRedeemer",out created)) {
    if(!created){MessageBox.Show("另一個兌換助手仍在執行，本次新版尚未啟動。\n\n請先關閉原本的助手視窗，再重新開啟這個程式。原本視窗不會因為下載或開啟新版而自動更新。\n\n本次版本："+Version+"\n程式位置："+Application.ExecutablePath,"請先關閉舊助手");return;}
    DesktopDpi();
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    try {Application.Run(new MainForm());} catch(Exception ex){MessageBox.Show(ex.Message,"兌換助手無法啟動");}
   }
  }
  [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SetProcessDPIAware")] static extern bool DesktopDpi();
 }
}
