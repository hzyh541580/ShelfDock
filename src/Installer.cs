using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

class Setup {
 static void RequestExit() { try { using (var signal = EventWaitHandle.OpenExisting("Local\\ShelfDock.Exit")) signal.Set(); Thread.Sleep(500); } catch (WaitHandleCannotBeOpenedException) {} }
 [STAThread] static void Main(string[] args) {
  if (args.Length == 2 && args[0] == "--verify-package") { using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShelfDock.exe")) using (var output = File.Create(args[1])) input.CopyTo(output); return; }
  if (args.Length == 1 && args[0] == "--test-uninstall-detection") { if (!IsUninstall(new string[0],"Uninstall.exe") || !IsUninstall(new[]{"/uninstall"},"Uninstall.exe") || IsUninstall(new string[0],"ShelfDock-Setup.exe") || IsUninstall(new[]{"/install"},"Uninstall.exe")) Environment.ExitCode = 1; return; }
  if (args.Length == 1 && args[0] == "--test-install-path") { string expected=Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ShelfDock"); if(!String.Equals(InstallFolder,expected,StringComparison.OrdinalIgnoreCase)) Environment.ExitCode=1; return; }
  Application.EnableVisualStyles();
  if (args.Length == 2 && args[0] == "--test-uninstall-ui") { using(var ui=new UninstallWindow()) { ui.Show(); Application.DoEvents(); using(var bitmap=new System.Drawing.Bitmap(ui.Width,ui.Height)) { ui.DrawToBitmap(bitmap,ui.ClientRectangle); bitmap.Save(args[1]); } ui.Close(); } return; }
  string folder = InstallFolder;
  string exe = Path.Combine(folder,"ShelfDock.exe"), setup = Path.Combine(folder,"Uninstall.exe");
  string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"拾放 ShelfDock.lnk");
  string uninstallShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"卸载拾放 ShelfDock.lnk");
  if (args.Length == 2 && args[0] == "--test-install") {
   string testRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(testRoot); string target = Path.Combine(testRoot,"ShelfDock.exe"); string testKey = @"Software\ShelfDock\InstallerTest-" + Guid.NewGuid().ToString("N");
   try { WritePayload(target); File.Copy(Assembly.GetExecutingAssembly().Location,Path.Combine(testRoot,"Uninstall.exe"),true); CreateShortcut(Path.Combine(testRoot,"ShelfDock.lnk"),target); CreateShortcut(Path.Combine(testRoot,"Uninstall.lnk"),Path.Combine(testRoot,"Uninstall.exe"),"/uninstall"); WriteRegistration(testKey,testRoot,target,Path.Combine(testRoot,"Uninstall.exe")); using (var key = Registry.CurrentUser.OpenSubKey(testKey)) if ((string)key.GetValue("DisplayVersion") != "1.19.0" || (string)key.GetValue("UninstallString") != "\""+Path.Combine(testRoot,"Uninstall.exe")+"\" /uninstall") throw new IOException("Registration test failed"); File.WriteAllText(Path.Combine(testRoot,"installer-test.txt"),"PASS: installer payload extraction, start menu uninstall shortcut, and per-user uninstall registration."); } catch (Exception e) { File.WriteAllText(Path.Combine(testRoot,"installer-test.txt"),"FAIL: " + e); Environment.ExitCode = 1; } finally { Registry.CurrentUser.DeleteSubKey(testKey,false); } return;
  }
  if (args.Length == 2 && args[0] == "--test-full-uninstall") { FullRemoval.Test(args[1]); return; }
  if (args.Length == 2 && args[0] == "--test-uninstall-handoff") { FullRemoval.TestHandoff(args[1]); return; }
  if (args.Length == 2 && args[0] == "--test-uninstall-launcher") { StartUninstallWorker("--test-uninstall-child \""+args[1]+"\""); return; }
  if (args.Length == 3 && args[0] == "--test-uninstall-child") { FullRemoval.TestHandoffChild(args[1],args[2]); return; }
  if (IsUninstall(args,Application.ExecutablePath) || (args.Length > 0 && args[0] == "--uninstall-ui")) FullRemoval.UseTemporaryWorkingDirectory();
  if (!EnsureAdministrator(args)) return;
  if (args.Length > 0 && args[0] == "--uninstall-ui") { if(args.Length == 2) FullRemoval.WaitForLauncher(args[1]); Application.Run(new UninstallWindow()); return; }

  if (IsUninstall(args,Application.ExecutablePath)) {
   StartUninstallWorker("--uninstall-ui"); return;
  }
  if (MessageBox.Show("将拾放安装到 Program Files，并添加开始菜单快捷方式。\n\n安装位置：" + folder, "安装拾放 ShelfDock", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
  try {
   Directory.CreateDirectory(folder); RequestExit();
   string pending = Path.Combine(folder,"ShelfDock.new"); WritePayload(pending);
   if (File.Exists(exe)) File.Replace(pending,exe,null); else File.Move(pending,exe);
   if (!String.Equals(Assembly.GetExecutingAssembly().Location,setup,StringComparison.OrdinalIgnoreCase)) File.Copy(Assembly.GetExecutingAssembly().Location, setup, true);
   CreateShortcut(shortcut,exe); CreateShortcut(uninstallShortcut,setup,"/uninstall"); if (ShelfDock.StartupRegistration.IsEnabled()) ShelfDock.StartupRegistration.Set(true,exe);
   WriteRegistration(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShelfDock",folder,exe,setup,true);
   try { RemoveLegacyInstallation(); } catch {}
   if (MessageBox.Show("安装完成。现在打开拾放？", "拾放", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) {
    try { ShelfDock.StandardUserLauncher.Start(exe,new string[0]); }
    catch(Exception ex) { MessageBox.Show("安装已完成，但无法以普通权限启动拾放："+ex.Message+"\n请从开始菜单打开拾放。","拾放"); }
   }
  } catch (Exception e) { MessageBox.Show("安装失败，请先退出运行中的拾放：" + e.Message, "拾放"); }
 }
 internal static string InstallFolder { get { return Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ShelfDock"); } }
 internal static Process StartUninstallWorker(string arguments) {
  FullRemoval.UseTemporaryWorkingDirectory();
  string worker=Path.Combine(Path.GetTempPath(),"ShelfDock-uninstall-"+Guid.NewGuid().ToString("N")+".exe");
  File.Copy(Application.ExecutablePath,worker);
  return Process.Start(new ProcessStartInfo(worker,arguments+" "+Process.GetCurrentProcess().Id) { UseShellExecute=false,WorkingDirectory=Path.GetTempPath() });
 }
 static void RemoveLegacyInstallation() { string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","ShelfDock"); FullRemoval.DeleteTree(path); Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShelfDock",false); }
 static bool EnsureAdministrator(string[] args) {
  using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent()) if(new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) return true;
  try { string arguments=String.Join(" ",Array.ConvertAll(args,a=>"\""+a.Replace("\"", "\\\"")+"\"")); Process.Start(new ProcessStartInfo(Application.ExecutablePath,arguments){UseShellExecute=true,Verb="runas"}); }
  catch(System.ComponentModel.Win32Exception ex) { if(ex.NativeErrorCode!=1223) MessageBox.Show(ex.Message,"无法请求管理员权限"); }
  return false;
 }
 internal static bool IsUninstall(string[] args,string executable) { return (args.Length > 0 && args[0] == "/uninstall") || (args.Length == 0 && String.Equals(Path.GetFileName(executable),"Uninstall.exe",StringComparison.OrdinalIgnoreCase)); }
 static void WritePayload(string path) { using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShelfDock.exe")) using (var output = File.Create(path)) input.CopyTo(output); }
 static void CreateShortcut(string path,string target,string arguments = "") {
  object shell = null, shortcut = null;
  try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic s = shell; shortcut = s.CreateShortcut(path); dynamic link = shortcut; link.TargetPath = target; link.Arguments = arguments; link.WorkingDirectory = Path.GetDirectoryName(target); link.Description = "拾放 ShelfDock"; link.IconLocation = target + ",0"; link.Save(); }
  finally { if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
 }
 static void WriteRegistration(string path,string folder,string exe,string setup,bool machine = false) { using (var key = (machine ? Registry.LocalMachine : Registry.CurrentUser).CreateSubKey(path)) { key.SetValue("DisplayName", "拾放 ShelfDock"); key.SetValue("DisplayVersion", "1.19.0"); key.SetValue("Publisher", "ShelfDock"); key.SetValue("InstallLocation", folder); key.SetValue("UninstallString", "\"" + setup + "\" /uninstall"); key.SetValue("DisplayIcon", exe); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord); } }
}
