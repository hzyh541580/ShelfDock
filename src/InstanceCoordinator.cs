using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace ShelfDock {
 // The identity is deliberately independent of the version, filename and install path.
 sealed class InstanceCoordinator : IDisposable {
  internal const string ProductName = "Local\\ShelfDock";
  readonly string name; readonly Version version; readonly Mutex mutex;
  bool ownsMutex; MemoryMappedFile info;
  internal EventWaitHandle ShowEvent, ExitEvent;
  internal InstanceCoordinator(string name, Version version) {
   this.name=name; this.version=version; mutex=new Mutex(false,name+".Singleton");
  }
  bool Acquire(int milliseconds) {
   try { ownsMutex=mutex.WaitOne(milliseconds); } catch(AbandonedMutexException) { ownsMutex=true; }
   return ownsMutex;
  }
  internal bool Enter(bool silent) {
   // Serialize startup so a second launch cannot mistake an initializing owner for a legacy build.
   using(var startup=new Mutex(false,name+".Startup")) {
    bool acquired=false;
    try {
     try { acquired=startup.WaitOne(12000); } catch(AbandonedMutexException) { acquired=true; }
     if(!acquired) throw new InvalidOperationException("拾放正在启动，请稍后重试。");
     return EnterCore(silent);
    } finally { if(acquired) startup.ReleaseMutex(); }
   }
  }
  bool EnterCore(bool silent) {
   var timeout=Stopwatch.StartNew();
   while (!Acquire(0)) {
    Version running=null;
    try { using(var shared=MemoryMappedFile.OpenExisting(name+".InstanceInfo",MemoryMappedFileRights.Read)) using(var reader=shared.CreateViewAccessor(0,32,MemoryMappedFileAccess.Read)) {
     if(reader.ReadInt32(0)==1) running=new Version(reader.ReadInt32(4),reader.ReadInt32(8),reader.ReadInt32(12),reader.ReadInt32(16));
    } } catch(System.IO.FileNotFoundException) { }
    if(running!=null && running>=version) {
     try { using(var show=EventWaitHandle.OpenExisting(name+".Show")) { if(!silent) show.Set(); } return false; }
     catch(WaitHandleCannotBeOpenedException) { }
    } else {
     // This also lets pre-1.23 versions retire through their existing exit event.
     try { using(var exit=EventWaitHandle.OpenExisting(name+".Exit")) exit.Set(); }
     catch(WaitHandleCannotBeOpenedException) { }
    }
    if(timeout.ElapsedMilliseconds>=10000) throw new InvalidOperationException("拾放的另一个实例尚未退出，请稍后重试。为避免重复托盘图标，本次不会启动第二个实例。");
    if(Acquire(50)) break;
   }
   ShowEvent=new EventWaitHandle(false,EventResetMode.AutoReset,name+".Show");
   ExitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,name+".Exit");
   ShowEvent.Reset(); ExitEvent.Reset();
   info=MemoryMappedFile.CreateOrOpen(name+".InstanceInfo",32);
   using(var writer=info.CreateViewAccessor()) {
    writer.Write(0,0); writer.Write(4,version.Major); writer.Write(8,version.Minor);
    writer.Write(12,Math.Max(0,version.Build)); writer.Write(16,Math.Max(0,version.Revision));
    writer.Write(20,Process.GetCurrentProcess().Id); writer.Write(0,1);
   }
   return true;
  }
  public void Dispose() {
   if(info!=null) info.Dispose();
   if(ShowEvent!=null) ShowEvent.Dispose(); if(ExitEvent!=null) ExitEvent.Dispose();
   if(ownsMutex) { mutex.ReleaseMutex(); ownsMutex=false; } mutex.Dispose();
  }
 }
}
