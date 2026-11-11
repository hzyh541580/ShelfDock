using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

class UninstallWindow : Form {
 readonly Label description = new Label(); readonly Button action = new Button(); readonly Button cancel = new Button(); readonly ProgressBar progress = new ProgressBar(); bool busy;
 public UninstallWindow() {
  Text="卸载拾放 ShelfDock"; ClientSize=new Size(520,290); StartPosition=FormStartPosition.CenterScreen; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; Font=new Font("Microsoft YaHei UI",10);
  description.SetBounds(24,24,472,165); description.Text="完全卸载拾放 ShelfDock\n\n将删除程序文件、设置、暂存记录、缓存图片、导出副本、快捷方式、开机启动项及软件注册表项。\n\n此操作不可恢复。暂存列表引用的外部原文件不会删除。";
  progress.SetBounds(24,200,472,18); action.SetBounds(284,240,102,32); action.Text="完全卸载"; cancel.SetBounds(396,240,100,32); cancel.Text="取消";
  Controls.AddRange(new Control[]{description,progress,action,cancel}); cancel.Click+=(s,e)=>Close(); action.Click+=Remove; FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
  FormClosed+=(s,e)=>FullRemoval.CleanupWorker();
 }
 void Remove(object sender,EventArgs e) {
  busy=true; action.Enabled=false; cancel.Enabled=false; progress.Style=ProgressBarStyle.Marquee; description.Text="正在关闭拾放并清理文件和注册表，请稍候……";
  var thread=new Thread(()=>{
   string error=null; try { FullRemoval.Run(); } catch(Exception ex) { error=ex.Message; }
   BeginInvoke((Action)(()=>{busy=false; progress.Style=ProgressBarStyle.Blocks; progress.Value=error==null?100:0; description.Text=error==null?"卸载完成。\n\n拾放的安装文件、应用数据、快捷方式和注册表项已清理。\n外部原文件保持不变。":"卸载未完成。以下项目无法清理：\n\n"+error+"\n\n请关闭占用文件的程序后重试。"; action.Enabled=error!=null; action.Text="重试"; cancel.Text="关闭"; cancel.Enabled=true;}));
  }); thread.IsBackground=true; thread.Start();
 }
}
static class FullRemoval {
 internal static void UseTemporaryWorkingDirectory() { Environment.CurrentDirectory=Path.GetTempPath(); }
 internal static void WaitForLauncher(string launcherId) {
  int id; if(!Int32.TryParse(launcherId,out id) || id==Process.GetCurrentProcess().Id) throw new ArgumentException("卸载启动进程编号无效。");
  try { using(var launcher=Process.GetProcessById(id)) if(!launcher.WaitForExit(10000)) throw new IOException("卸载启动程序尚未退出，请关闭旧卸载窗口后重试。"); } catch(ArgumentException) {}
 }
 internal static void DeleteTree(string path) {
  if(!Directory.Exists(path)) return;
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) { Directory.Delete(path); return; }
  foreach(string file in Directory.GetFiles(path)) { File.SetAttributes(file,FileAttributes.Normal); File.Delete(file); }
  foreach(string child in Directory.GetDirectories(path)) DeleteTree(child);
  Directory.Delete(path);
 }
 static void Remove(string install,string data,string[] shortcuts,string key) {
  // Never enumerate items.json paths: they point to the user's original files.
  DeleteTree(install); DeleteTree(data);
  foreach(string path in shortcuts) if(File.Exists(path)) File.Delete(path);
  Registry.CurrentUser.DeleteSubKeyTree(key,false);
 }
 internal static void Run() {
  // The shell shortcut may start Uninstall.exe with the install folder as its working directory.
  // Move away before deleting that folder, otherwise Windows keeps it locked by this process.
  UseTemporaryWorkingDirectory();
  try { using(var exit=EventWaitHandle.OpenExisting("Local\\ShelfDock.Exit")) exit.Set(); } catch(WaitHandleCannotBeOpenedException) {}
  using(var mutex=new Mutex(false,"Local\\ShelfDock.Singleton")) {
   bool held=false; try { try { held=mutex.WaitOne(10000); } catch(AbandonedMutexException) { held=true; } if(!held) throw new IOException("拾放仍在运行，请从托盘退出后重试。");
    string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), programs=Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    Remove(Setup.InstallFolder,Path.Combine(local,"ShelfDock"),new[]{Path.Combine(programs,"拾放 ShelfDock.lnk"),Path.Combine(programs,"卸载拾放 ShelfDock.lnk"),ShelfDock.StartupRegistration.ShortcutPath},@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShelfDock");
    using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32)) { machine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShelfDock",false); machine.DeleteSubKeyTree(@"Software\ShelfDock",false); }
    Registry.CurrentUser.DeleteSubKeyTree(@"Software\ShelfDock",false);
    using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) if(run!=null) run.DeleteValue("ShelfDock",false);
   } finally { if(held) mutex.ReleaseMutex(); }
  }
 }
 internal static void CleanupWorker() {
  string self=Application.ExecutablePath;
  if(!Path.GetFileName(self).StartsWith("ShelfDock-uninstall-",StringComparison.Ordinal) || !String.Equals(Path.GetDirectoryName(self).TrimEnd('\\'),Path.GetTempPath().TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)) return;
  string code="$p="+Process.GetCurrentProcess().Id+"; while(Get-Process -Id $p -ErrorAction SilentlyContinue){Start-Sleep -Milliseconds 150}; Remove-Item -LiteralPath '"+self.Replace("'","''")+"' -Force -ErrorAction SilentlyContinue";
  Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(code))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetTempPath()});
 }
 internal static void TestHandoff(string root) {
  root=Path.GetFullPath(root); Directory.CreateDirectory(root); string install=Path.Combine(root,"install"); Directory.CreateDirectory(install);
  string launcher=Path.Combine(install,"Uninstall.exe"),result=Path.Combine(root,"handoff-result.txt");
  try {
   File.Copy(Application.ExecutablePath,launcher,true);
   if(File.Exists(result)) File.Delete(result);
   using(var process=Process.Start(new ProcessStartInfo(launcher,"--test-uninstall-launcher \""+root+"\""){UseShellExecute=false,WorkingDirectory=install})) {
    if(!process.WaitForExit(15000) || process.ExitCode!=0) throw new Exception("Uninstall launcher did not exit successfully");
   }
   var clock=Stopwatch.StartNew(); while(!File.Exists(result) && clock.ElapsedMilliseconds<20000) Thread.Sleep(100);
   if(!File.Exists(result) || !File.ReadAllText(result).StartsWith("PASS:") || Directory.Exists(install)) throw new Exception(File.Exists(result)?File.ReadAllText(result):"Uninstall worker did not report completion");
  } catch(Exception ex) { File.WriteAllText(result,"FAIL: "+ex); Environment.ExitCode=1; }
 }
 internal static void TestHandoffChild(string root,string launcherId) {
  string result=Path.Combine(Path.GetFullPath(root),"handoff-result.txt");
  try { UseTemporaryWorkingDirectory(); WaitForLauncher(launcherId); DeleteTree(Path.Combine(Path.GetFullPath(root),"install")); File.WriteAllText(result,"PASS: temporary worker launched from the install directory; launcher exited; install executable and directory removed without a working-directory lock."); }
  catch(Exception ex) { File.WriteAllText(result,"FAIL: "+ex); Environment.ExitCode=1; }
  finally { CleanupWorker(); }
 }
 internal static void Test(string root) {
  root=Path.GetFullPath(root); Directory.CreateDirectory(root); string install=Path.Combine(root,"install"),data=Path.Combine(root,"data"),original=Path.Combine(root,"original.txt"),shortcut=Path.Combine(root,"test.lnk"),key=@"Software\ShelfDock-UninstallTest-"+Guid.NewGuid().ToString("N");
  string previousDirectory=Environment.CurrentDirectory;
  try { Directory.CreateDirectory(install); Directory.CreateDirectory(Path.Combine(data,"images")); File.WriteAllText(Path.Combine(install,"Uninstall.exe"),"test"); File.WriteAllText(original,"preserve"); File.WriteAllText(Path.Combine(data,"items.json"),original); File.WriteAllText(Path.Combine(data,"images","cached.png"),"cache"); File.WriteAllText(shortcut,"shortcut"); using(var k=Registry.CurrentUser.CreateSubKey(key))k.SetValue("test",1);
   Environment.CurrentDirectory=install; UseTemporaryWorkingDirectory();
   Remove(install,data,new[]{shortcut},key); using(var k=Registry.CurrentUser.OpenSubKey(key)) if(k!=null) throw new Exception("Registry remains");
   if(Directory.Exists(install)||Directory.Exists(data)||File.Exists(shortcut)||File.ReadAllText(original)!="preserve") throw new Exception("Removal verification failed");
   Remove(install,data,new[]{shortcut},key); File.WriteAllText(Path.Combine(root,"full-uninstall-test.txt"),"PASS: application tree, nested cache, shortcuts and registry removed; external original preserved; repeated removal succeeds.");
  } catch(Exception ex) { File.WriteAllText(Path.Combine(root,"full-uninstall-test.txt"),"FAIL: "+ex); Environment.ExitCode=1; } finally { Environment.CurrentDirectory=previousDirectory; Registry.CurrentUser.DeleteSubKeyTree(key,false); }
 }
}
