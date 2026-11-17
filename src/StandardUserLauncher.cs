using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ShelfDock {
 internal static class StandardUserLauncher {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct StartupInfo {
   public int Size; public string Reserved, Desktop, Title;
   public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
   public short Show, ReservedSize; public IntPtr ReservedData, Input, Output, Error;
  }
  [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
  [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out int process);
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int process);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool DuplicateTokenEx(IntPtr token,uint access,IntPtr attributes,int level,int type,out IntPtr duplicate);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int information,out int value,int size,out int required);
  [DllImport("userenv.dll",SetLastError=true)] static extern bool CreateEnvironmentBlock(out IntPtr environment,IntPtr token,bool inherit);
  [DllImport("userenv.dll")] static extern bool DestroyEnvironmentBlock(IntPtr environment);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessWithTokenW(IntPtr token,uint logon,string application,StringBuilder command,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
  internal static bool IsElevated {
   get { using(var identity=WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
  }
  internal static bool IsShellElevated {
   get {
    IntPtr shell=GetShellWindow(); if(shell==IntPtr.Zero) throw new InvalidOperationException("找不到桌面资源管理器，请从开始菜单打开拾放。");
    int shellId; GetWindowThreadProcessId(shell,out shellId);
    IntPtr process=OpenProcess(0x1000,false,shellId),token=IntPtr.Zero;
    if(process==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法读取桌面资源管理器的权限级别。");
    try {
     if(!OpenProcessToken(process,0x0008,out token)) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法读取桌面资源管理器的权限级别。");
     int elevated,required;
     if(!GetTokenInformation(token,20,out elevated,4,out required)) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法读取桌面资源管理器的权限级别。");
     return elevated!=0;
    } finally { if(token!=IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
   }
  }
  internal static string QuoteArgument(string value) {
   var result=new StringBuilder("\""); int slashes=0;
   foreach(char c in value) {
    if(c=='\\') { slashes++; continue; }
    if(c=='\"') result.Append('\\',slashes*2+1); else result.Append('\\',slashes);
    slashes=0; result.Append(c);
   }
   return result.Append('\\',slashes*2).Append('"').ToString();
  }
  static string CommandLine(string executable,string[] arguments) {
   var command=new StringBuilder(QuoteArgument(executable));
   foreach(string argument in arguments) command.Append(' ').Append(QuoteArgument(argument));
   return command.ToString();
  }
  // Start at the desktop shell's integrity level. Elevation uses the normal UAC consent flow.
  internal static int Start(string executable,string[] arguments) {
   executable=Path.GetFullPath(executable);
   bool shellElevated=IsShellElevated;
   string command=CommandLine(executable,arguments);
   if(IsElevated==shellElevated || shellElevated) {
    var start=new ProcessStartInfo(executable,command.Substring(QuoteArgument(executable).Length).TrimStart()) {
     UseShellExecute=shellElevated && !IsElevated,
     WorkingDirectory=Path.GetDirectoryName(executable)
    };
    if(shellElevated && !IsElevated) start.Verb="runas";
    using(var child=Process.Start(start)) return child.Id;
   }
   return StartWithShellToken(executable,command);
  }
  static int StartWithShellToken(string executable,string command) {
   int shellId;
   IntPtr shell=GetShellWindow(); if(shell==IntPtr.Zero) throw new InvalidOperationException("找不到桌面资源管理器，请从开始菜单打开拾放。");
   GetWindowThreadProcessId(shell,out shellId);
   IntPtr process=OpenProcess(0x1000,false,shellId),token=IntPtr.Zero,primary=IntPtr.Zero,environment=IntPtr.Zero;
   if(process==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
   try {
    if(!OpenProcessToken(process,0x000A,out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
    int elevated,required;
    if(!GetTokenInformation(token,20,out elevated,4,out required)) throw new Win32Exception(Marshal.GetLastWin32Error());
    if(elevated!=0) throw new InvalidOperationException("桌面资源管理器权限级别已变化，请重新打开拾放。");
    if(!DuplicateTokenEx(token,0x02000000,IntPtr.Zero,2,1,out primary)) throw new Win32Exception(Marshal.GetLastWin32Error());
    if(!CreateEnvironmentBlock(out environment,primary,false)) throw new Win32Exception(Marshal.GetLastWin32Error());
    var startup=new StartupInfo { Size=Marshal.SizeOf(typeof(StartupInfo)), Desktop="winsta0\\default" }; ProcessInfo child;
    if(!CreateProcessWithTokenW(primary,0,executable,new StringBuilder(command),0x00000400,environment,Path.GetDirectoryName(executable),ref startup,out child)) throw new Win32Exception(Marshal.GetLastWin32Error());
    try { return child.ProcessId; } finally { CloseHandle(child.Thread); CloseHandle(child.Process); }
   } finally { if(environment!=IntPtr.Zero) DestroyEnvironmentBlock(environment); if(primary!=IntPtr.Zero) CloseHandle(primary); if(token!=IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
  }
 }
}
