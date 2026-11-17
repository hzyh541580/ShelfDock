using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShelfDock {
 // One Shell identity across portable/installed builds and every future version.
 sealed class StableTrayIcon : NativeWindow, IDisposable {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct NotifyData {
   public int Size; public IntPtr Window; public uint Id,Flags,Callback; public IntPtr Icon;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
   public uint State,StateMask;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
   public uint Version;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string InfoTitle;
   public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
  }
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint operation,ref NotifyData data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
  const int CallbackMessage=0x8001;
  readonly int taskbarCreated; bool visible,registered,disposed;
  readonly Guid identity;
  internal string Text; internal Icon Icon; internal ContextMenuStrip ContextMenuStrip;
  internal event EventHandler DoubleClick;
  internal static readonly Guid Identity=new Guid("8eb42a39-cfe9-42d5-858f-59fe04521693");
  internal StableTrayIcon() : this(Identity) {}
  internal StableTrayIcon(Guid identity) {
   this.identity=identity;
   CreateHandle(new CreateParams { Caption="ShelfDock.TrayHost",ExStyle=0x80 });
   taskbarCreated=RegisterWindowMessage("TaskbarCreated");
  }
  NotifyData Data() { return new NotifyData { Size=Marshal.SizeOf(typeof(NotifyData)),Window=Handle,Id=1,Guid=identity,Flags=0x20 }; }
  internal bool Visible {
   get { return visible; }
   set { if(disposed) return; visible=value; if(value) Add(); else Remove(); }
  }
  void Add() {
   if(registered || disposed || Icon==null) return;
   var data=Data();
   // Clear an orphan of this same GUID before adding; no unrelated icons are touched.
   Shell_NotifyIcon(2,ref data);
   data.Flags|=1|2|4|0x80; data.Callback=CallbackMessage; data.Icon=Icon.Handle; data.Tip=Text;
   registered=Shell_NotifyIcon(0,ref data);
   if(!registered) throw new InvalidOperationException("无法创建拾放托盘图标。");
   data.Version=4; Shell_NotifyIcon(4,ref data);
  }
  void Remove() { if(registered) { var data=Data(); Shell_NotifyIcon(2,ref data); registered=false; } }
  internal void ShowBalloonTip(int timeout,string title,string text,ToolTipIcon icon) {
   if(!registered) return;
   var data=Data(); data.Flags|=0x10; data.InfoTitle=title; data.Info=text; data.Version=(uint)timeout; data.InfoFlags=(uint)icon;
   Shell_NotifyIcon(1,ref data);
  }
  protected override void WndProc(ref Message m) {
   if(m.Msg==taskbarCreated) { registered=false; if(visible) Add(); }
   else if(m.Msg==CallbackMessage) {
    int action=m.LParam.ToInt32() & 0xffff;
    if(action==0x007b && ContextMenuStrip!=null) {
     SetForegroundWindow(Handle);
     int packed=unchecked((int)m.WParam.ToInt64());
     var point=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));
     if(point.X==-1 && point.Y==-1) point=Cursor.Position;
     ContextMenuStrip.Show(point);
     PostMessage(Handle,0,IntPtr.Zero,IntPtr.Zero);
    } else if(action==0x0203 || action==0x0401) {
     var handler=DoubleClick; if(handler!=null) handler(this,EventArgs.Empty);
    }
   }
   base.WndProc(ref m);
  }
  public void Dispose() { if(disposed) return; Remove(); visible=false; disposed=true; if(ContextMenuStrip!=null) ContextMenuStrip.Dispose(); DestroyHandle(); }
 }
}
