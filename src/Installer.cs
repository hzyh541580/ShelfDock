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
  Application.EnableVisualStyles();
  string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ShelfDock");
  string exe = Path.Combine(folder,"ShelfDock.exe"), setup = Path.Combine(folder,"Uninstall.exe");
  string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"拾放 ShelfDock.lnk");
  if (args.Length == 2 && args[0] == "--test-install") {
   string testRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(testRoot); string target = Path.Combine(testRoot,"ShelfDock.exe"); string testKey = @"Software\ShelfDock\InstallerTest-" + Guid.NewGuid().ToString("N");
   try { WritePayload(target); CreateShortcut(Path.Combine(testRoot,"ShelfDock.lnk"),target); WriteRegistration(testKey,testRoot,target,Path.Combine(testRoot,"Uninstall.exe")); using (var key = Registry.CurrentUser.OpenSubKey(testKey)) if ((string)key.GetValue("DisplayVersion") != "1.17.0") throw new IOException("Registration test failed"); File.WriteAllText(Path.Combine(testRoot,"installer-test.txt"),"PASS: installer payload extraction, shortcut COM creation and per-user uninstall registration round trip."); } catch (Exception e) { File.WriteAllText(Path.Combine(testRoot,"installer-test.txt"),"FAIL: " + e); Environment.ExitCode = 1; } finally { Registry.CurrentUser.DeleteSubKey(testKey,false); } return;
  }
  if (args.Length == 2 && args[0] == "--cleanup") {
   try { int parent = Int32.Parse(args[1]); try { using (var p = Process.GetProcessById(parent)) { if (!p.WaitForExit(10000)) return; } } catch (ArgumentException) {} RequestExit(); for (int n = 0; n < 20; n++) { try { if (File.Exists(setup)) File.Delete(setup); if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder); break; } catch (IOException) { Thread.Sleep(200); } } } catch {} return;
  }
  if (args.Length > 0 && args[0] == "/uninstall") {
   if (MessageBox.Show("卸载拾放？暂存数据将保留。", "卸载拾放", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
   try { RequestExit(); ShelfDock.StartupRegistration.Set(false,exe); if (File.Exists(shortcut)) File.Delete(shortcut); if (File.Exists(exe)) File.Delete(exe); Registry.CurrentUser.DeleteSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShelfDock",false); string worker = Path.Combine(Path.GetTempPath(),"ShelfDock-cleanup-" + Guid.NewGuid().ToString("N") + ".exe"); File.Copy(Assembly.GetExecutingAssembly().Location,worker); Process.Start(new ProcessStartInfo(worker,"--cleanup " + Process.GetCurrentProcess().Id) { UseShellExecute = true }); MessageBox.Show("卸载完成。暂存数据保留在本机：\n" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShelfDock"), "拾放"); } catch (Exception e) { MessageBox.Show("卸载失败，请先退出拾放：" + e.Message, "拾放"); } return;
  }
  if (MessageBox.Show("将拾放安装到当前用户账户，并添加开始菜单快捷方式。\n\n安装位置：" + folder, "安装拾放 ShelfDock", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
  try {
   Directory.CreateDirectory(folder); RequestExit();
   string pending = Path.Combine(folder,"ShelfDock.new"); WritePayload(pending);
   if (File.Exists(exe)) File.Replace(pending,exe,null); else File.Move(pending,exe);
   if (!String.Equals(Assembly.GetExecutingAssembly().Location,setup,StringComparison.OrdinalIgnoreCase)) File.Copy(Assembly.GetExecutingAssembly().Location, setup, true);
   CreateShortcut(shortcut,exe); if (ShelfDock.StartupRegistration.IsEnabled()) ShelfDock.StartupRegistration.Set(true,exe);
   WriteRegistration(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShelfDock",folder,exe,setup);
   if (MessageBox.Show("安装完成。现在打开拾放？", "拾放", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) System.Diagnostics.Process.Start(exe);
  } catch (Exception e) { MessageBox.Show("安装失败，请先退出运行中的拾放：" + e.Message, "拾放"); }
 }
 static void WritePayload(string path) { using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShelfDock.exe")) using (var output = File.Create(path)) input.CopyTo(output); }
 static void CreateShortcut(string path,string target) {
  object shell = null, shortcut = null;
  try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic s = shell; shortcut = s.CreateShortcut(path); dynamic link = shortcut; link.TargetPath = target; link.WorkingDirectory = Path.GetDirectoryName(target); link.Description = "拾放 ShelfDock"; link.IconLocation = target + ",0"; link.Save(); }
  finally { if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
 }
 static void WriteRegistration(string path,string folder,string exe,string setup) { using (var key = Registry.CurrentUser.CreateSubKey(path)) { key.SetValue("DisplayName", "拾放 ShelfDock"); key.SetValue("DisplayVersion", "1.17.0"); key.SetValue("Publisher", "ShelfDock"); key.SetValue("InstallLocation", folder); key.SetValue("UninstallString", "\"" + setup + "\" /uninstall"); key.SetValue("DisplayIcon", exe); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord); } }
}
