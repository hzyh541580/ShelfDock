using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ShelfDock {
 internal static class StartupRegistration {
  internal static string ShortcutPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ShelfDock-Silent.lnk"); } }
  internal static bool IsEnabled() { return IsEnabled(ShortcutPath); }
  internal static bool IsEnabled(string path) {
   if (!File.Exists(path)) return false;
   object shell = null, shortcut = null;
   try {
    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic host = shell;
    shortcut = host.CreateShortcut(path); dynamic link = shortcut;
    return File.Exists((string)link.TargetPath) && (string)link.Arguments == "--silent";
   } catch { return false; }
   finally { Release(shortcut); Release(shell); }
  }
  internal static void Set(bool enabled, string executable) { Set(enabled,executable,ShortcutPath); }
  internal static void Set(bool enabled, string executable, string path) {
   if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
   executable = Path.GetFullPath(executable);
   if (!File.Exists(executable)) throw new FileNotFoundException("启动程序不存在。",executable);
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   string pending = Path.Combine(Path.GetDirectoryName(path),Guid.NewGuid().ToString("N")+".lnk");
   object shell = null, shortcut = null;
   try {
    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic host = shell;
    shortcut = host.CreateShortcut(pending); dynamic link = shortcut;
    link.TargetPath = executable; link.Arguments = "--silent"; link.WorkingDirectory = Path.GetDirectoryName(executable);
    link.Description = "拾放 ShelfDock · 静默启动"; link.IconLocation = executable + ",0"; link.Save();
    if (!IsEnabled(pending)) throw new IOException("无法验证开机启动快捷方式。");
    if (File.Exists(path)) File.Replace(pending,path,null); else File.Move(pending,path);
   } finally { Release(shortcut); Release(shell); if (File.Exists(pending)) File.Delete(pending); }
  }
  static void Release(object instance) { if (instance != null) Marshal.FinalReleaseComObject(instance); }
 }
}
