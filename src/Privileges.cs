using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace WwmRedeemer {
 public static class Privileges {
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int id);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int info,out int value,int length,out int returned);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
  public static bool Elevated(int id){
   IntPtr process=OpenProcess(0x1000,false,id),token=IntPtr.Zero;
   try {
    if(process==IntPtr.Zero||!OpenProcessToken(process,8,out token))throw new Win32Exception(Marshal.GetLastWin32Error(),"無法讀取程式權限。");
    int value,returned;if(!GetTokenInformation(token,20,out value,4,out returned))throw new Win32Exception(Marshal.GetLastWin32Error(),"無法讀取程式權限。");
    return value!=0;
   }finally{if(token!=IntPtr.Zero)CloseHandle(token);if(process!=IntPtr.Zero)CloseHandle(process);}
  }
  public static bool CurrentElevated(){return Elevated(Process.GetCurrentProcess().Id);}
  public static bool Blocked(bool assistantElevated,bool targetElevated){return targetElevated&&!assistantElevated;}
  public static void RequireInput(int targetId){
   if(Blocked(CurrentElevated(),Elevated(targetId)))throw new InvalidOperationException("目標程式以系統管理員執行，助手目前為一般權限，Windows 會阻擋自動輸入。請關閉助手，右鍵執行檔選「以系統管理員身分執行」，確認 Windows 提示後再試。尚未輸入或送出兌換。");
  }
 }
}
