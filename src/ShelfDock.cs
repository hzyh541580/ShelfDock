using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace ShelfDock {
 // Record the actual button-down position, even between edge polling ticks.
 // A click/hold is not a drag until it leaves Windows' drag tolerance rectangle.
 sealed class MouseDragTracker : IDisposable {
  delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
  [StructLayout(LayoutKind.Sequential)] struct MouseData { public Point Position; public uint MouseDataValue, Flags, Time; public UIntPtr ExtraInfo; }
  [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
  [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
  [DllImport("kernel32.dll", CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
  readonly MouseHook callback; IntPtr hook; bool pressed; Point origin;
  internal bool IsDragging { get; private set; }
  internal MouseDragTracker(bool monitor) { if (monitor) { callback = OnMouse; hook = SetWindowsHookEx(14,callback,GetModuleHandle(null),0); if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法监听鼠标拖动"); } }
  internal void Record(bool leftHeld, Point pointer) {
   if (!leftHeld) { pressed = false; IsDragging = false; return; }
   if (!pressed) { pressed = true; origin = pointer; IsDragging = false; return; }
   var size = SystemInformation.DragSize;
   if (!new Rectangle(origin.X-size.Width/2,origin.Y-size.Height/2,size.Width,size.Height).Contains(pointer)) IsDragging = true;
  }
  IntPtr OnMouse(int code, IntPtr message, IntPtr data) {
   if (code >= 0) { int kind = message.ToInt32(); if (kind == 0x0201 || kind == 0x0202 || (kind == 0x0200 && pressed)) { var mouse = (MouseData)Marshal.PtrToStructure(data,typeof(MouseData)); Record(kind != 0x0202,mouse.Position); } }
   return CallNextHookEx(hook,code,message,data);
  }
  public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
 }
 [DataContract] public class Entry {
  [DataMember] public string Kind;
  [DataMember] public string Value;
  [DataMember] public string Name;
  [DataMember(EmitDefaultValue=false)] public string[] Paths;
 }
 [DataContract] public class Preferences {
  [DataMember] public bool EdgeEnabled = true;
  [DataMember] public string EdgeSide = "right";
  [DataMember(EmitDefaultValue=false)] public int EdgePercent = 10;
  [DataMember(EmitDefaultValue=false)] public bool StartWithWindows;
  [DataMember] public int EmptyCloseDelayHalfSeconds = 1;
  [DataMember] public bool CloseToTray = true;
  [OnDeserializing] void OnDeserializing(StreamingContext context) { EmptyCloseDelayHalfSeconds = 1; CloseToTray = true; }
 }
 static class Program {
  internal static EventWaitHandle ShowEvent;
  internal static EventWaitHandle ExitEvent;
  [STAThread] static void Main(string[] args) {
   if (args.Length == 2 && args[0] == "--self-test") { Tests.Run(args[1]); return; }
   bool silent = args.Any(a => a == "--silent");
   bool created; using (var mutex = new Mutex(true, "Local\\ShelfDock.Singleton", out created)) {
    if (!created) {
     bool sameVersion = false;
     try { using (var marker = EventWaitHandle.OpenExisting("Local\\ShelfDock.Version.1.18")) sameVersion = true; } catch (WaitHandleCannotBeOpenedException) {}
     if (sameVersion) { if (!silent) { try { using (var show = EventWaitHandle.OpenExisting("Local\\ShelfDock.Show")) show.Set(); } catch { MessageBox.Show("拾放已经在运行，请查看系统托盘。", "拾放"); } } return; }
     try { using (var exit = EventWaitHandle.OpenExisting("Local\\ShelfDock.Exit")) exit.Set(); if (!mutex.WaitOne(5000)) { MessageBox.Show("旧版拾放仍在运行，请从系统托盘退出后重新打开。", "拾放"); return; } }
     catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("拾放正在启动或旧版未正常响应，请稍后重试。", "拾放"); return; }
     catch (AbandonedMutexException) {}
    }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    using (var version = new EventWaitHandle(false, EventResetMode.ManualReset,"Local\\ShelfDock.Version.1.18")) using (ShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset,"Local\\ShelfDock.Show")) using (ExitEvent = new EventWaitHandle(false, EventResetMode.AutoReset,"Local\\ShelfDock.Exit")) {
     string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShelfDock");
     for (int i=0; i+1<args.Length; i++) if (args[i] == "--data-dir") dataRoot = Path.GetFullPath(args[++i]);
     Application.Run(new Shelf(dataRoot, silent));
    }
   }
  }
 }
 public class Shelf : Form {
  [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
  readonly string root;
  Preferences preferences = new Preferences();
  readonly List<Entry> entries = new List<Entry>();
  readonly FlowLayoutPanel list = new FlowLayoutPanel();
  readonly Label count = new Label(); readonly Label empty = new Label();
  readonly NotifyIcon tray = new NotifyIcon(); readonly System.Windows.Forms.Timer edge = new System.Windows.Forms.Timer(); readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer(); readonly System.Windows.Forms.Timer emptyCloseTimer = new System.Windows.Forms.Timer();
  readonly ToolTip tips = new ToolTip();
  readonly HashSet<Entry> selected = new HashSet<Entry>();
  bool quitting, dragging; Point dragStart; bool ownDrag;
  bool edgePreview, emptyShelfClosePending; readonly bool startHidden; bool initialVisibilityHandled; double animationFrom, animationTo; readonly System.Diagnostics.Stopwatch emptyCloseClock = new System.Diagnostics.Stopwatch();
  internal Func<Point> PointerPosition = () => Cursor.Position;
  bool edgeDragEnteredWindow, edgeDragDismissed, edgeReactivationBlocked; int edgeDragClosestDistance;
  readonly MouseDragTracker mouseDrag = new MouseDragTracker(true);
  Color bg = Color.FromArgb(246,248,249), ink = Color.FromArgb(32,43,48), muted = Color.FromArgb(107,120,126), accent = Color.FromArgb(0,122,111);
  public Shelf() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShelfDock")) {}
  public Shelf(string dataRoot) : this(dataRoot, false) {}
  public Shelf(string dataRoot, bool silentStart) {
   root = dataRoot; startHidden = silentStart; ShowInTaskbar = !silentStart;
   Directory.CreateDirectory(root); LoadItems(); LoadPreferences(); preferences.StartWithWindows = StartupRegistration.IsEnabled();
   Text = "拾放 ShelfDock 1.18"; Size = new Size(370, 590); MinimumSize = new Size(320, 350); StartPosition = FormStartPosition.Manual;
   var area = Screen.PrimaryScreen.WorkingArea; Location = new Point(preferences.EdgeSide == "left" ? area.Left + 16 : area.Right - Width - 16, area.Top + Math.Max(10, (area.Height - Height)/2));
   Font = new Font("Microsoft YaHei UI", 9F); BackColor = bg; ForeColor = ink; TopMost = true; AllowDrop = true; KeyPreview = true;
   KeyDown += (s,e) => { if (e.Control && e.KeyCode == Keys.V) { Paste(); e.Handled = true; } if (e.Control && e.KeyCode == Keys.A) { foreach (var en in entries) selected.Add(en); PaintSelection(); e.Handled = true; } if (e.Control && e.KeyCode == Keys.C && selected.Count > 0) { try { Clipboard.SetDataObject(MakeSelectionData(selected.ToArray()),true); } catch (Exception ex) { MessageBox.Show(ex.Message,"拾放"); } e.Handled = true; } if (e.KeyCode == Keys.Delete) { RemoveEntries(selected.ToArray()); e.Handled = true; } if (e.KeyCode == Keys.Escape) Hide(); };
   var header = new Panel { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 110, BackColor = Color.White, Padding = new Padding(18, 15, 18, 8) };
   var title = new Label { Text = "拾放", Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), AutoSize = true, Location = new Point(15, 10), ForeColor = ink };
   var sub = new Label { Text = "S H E L F D O C K", AutoSize = true, Location = new Point(18, 52), ForeColor = muted, Font = new Font("Segoe UI", 8F) };
   count.AutoSize = true; count.Location = new Point(19, 80); count.ForeColor = muted;
   
   var pin = Button("", "切换始终置顶", 310, 14); pin.Anchor = AnchorStyles.Top | AnchorStyles.Right; pin.Paint += (s,e) => DrawPin(e.Graphics, pin.ClientRectangle, TopMost); pin.Click += (s,e) => { TopMost = !TopMost; pin.Invalidate(); };
   var settings = Button("⚙", "设置", 270, 14); settings.Anchor = AnchorStyles.Top | AnchorStyles.Right; settings.Click += (s,e) => ShowSettings();
   header.Controls.AddRange(new Control[]{title,sub,count,settings,pin});
   var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(12, 9, 12, 9), BackColor = Color.White };
   var paste = new Button { Text = "＋  从剪贴板添加", AutoSize = false, Width = 160, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = accent, ForeColor = Color.White, Cursor = Cursors.Hand, Dock = DockStyle.Left };
   paste.FlatAppearance.BorderSize = 0; paste.Click += (s,e) => Paste();
   var clear = new Button { Text = "清空", Width = 70, Height = 36, FlatStyle = FlatStyle.Flat, ForeColor = muted, Dock = DockStyle.Right, Cursor = Cursors.Hand };
   clear.FlatAppearance.BorderColor = Color.FromArgb(220,226,228); clear.Click += (s,e) => { if (entries.Count > 0 && MessageBox.Show("清空暂存列表？原始文件不会删除。", "拾放", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { entries.Clear(); Save(); RefreshItems(); } };
   footer.Controls.Add(clear); footer.Controls.Add(paste);
   list.Dock = DockStyle.Fill; list.Padding = new Padding(12, 12, 12, 0); list.AutoScroll = true; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.AllowDrop = true; list.BackColor = bg;
   empty.Text = "把文件、文字或图片拖到这里\n\n切换窗口，再拖到目的地\n\nCtrl + Alt + Space 随时唤出\n按住 Ctrl 可选择多项"; empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = muted; empty.Size = new Size(300, 230); empty.AllowDrop = true;
   Controls.Add(list); Controls.Add(footer); Controls.Add(header);
   DragEnter += EnterDrop; DragDrop += Drop; list.DragEnter += EnterDrop; list.DragDrop += Drop; empty.DragEnter += EnterDrop; empty.DragDrop += Drop;
   Resize += (s,e) => RefreshItems();
   try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {} tray.Text = "拾放 ShelfDock"; tray.Icon = Icon; tray.Visible = true;
   var menu = new ContextMenuStrip(); menu.Items.Add("显示拾放   Ctrl+Alt+Space", null, (s,e) => ShowShelf()); menu.Items.Add("设置", null, (s,e) => { ShowShelf(); ShowSettings(); }); menu.Items.Add("使用帮助", null, (s,e) => MessageBox.Show("拖入内容暂存，再拖出到目标应用。\n文件只记录原位置，拖出采用复制，不移动原文件。\nCtrl+单击多选；Ctrl+A 全选；Ctrl+C 复制；Delete 移除。\n双击项目复制到剪贴板。关闭窗口行为可在设置中切换。\n同一次加入的多个文件会组成合集，可整体复制或拖出。成功拖出后仅移除对应暂存项目。\n在设置中可开关边缘唤出并选择屏幕左侧或右侧。\n文字与图片存储在本机，程序不联网。\n\n独立开发的 Windows 暂存工具，与 Yoink 无关联。", "拾放 · 使用帮助")); menu.Items.Add("退出", null, (s,e) => { quitting = true; Close(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += (s,e) => ShowShelf();
   edge.Interval = 50; edge.Tick += (s,e) => { if (Program.ExitEvent != null && Program.ExitEvent.WaitOne(0)) { quitting = true; Close(); return; } if (Program.ShowEvent != null && Program.ShowEvent.WaitOne(0)) ShowShelf(); UpdateEdgeVisibility((Control.MouseButtons & MouseButtons.Left) != 0, Cursor.Position); }; edge.Start();
   emptyCloseTimer.Interval = 50; emptyCloseTimer.Tick += (s,e) => CheckEmptyShelfClose();
   animation.Interval = 15; animation.Tick += (s,e) => AdvanceAnimation();
   HandleCreated += (s,e) => { if (!RegisterHotKey(Handle, 1, 2 | 4, (uint)Keys.Space) && !startHidden) tray.ShowBalloonTip(3000, "拾放", "快捷键 Ctrl+Alt+Space 已被其他程序占用。", ToolTipIcon.Info); };
   HandleDestroyed += (s,e) => UnregisterHotKey(Handle,1);
   FormClosing += (s,e) => { if (!quitting && e.CloseReason == CloseReason.UserClosing && preferences.CloseToTray) { e.Cancel = true; Hide(); } else { quitting = true; Save(); edge.Stop(); animation.Stop(); tray.Visible = false; tray.Dispose(); } };
   RefreshItems();
  }
  protected override void WndProc(ref Message m) { if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { if (Visible) Hide(); else ShowShelf(); } base.WndProc(ref m); }
  internal static bool AtEdge(Point p, Rectangle screen, string side, int percent) { int width = Math.Max(1, (int)Math.Ceiling(screen.Width * percent / 100.0)); return screen.Contains(p) && (side == "left" ? p.X < screen.Left+width : p.X >= screen.Right-width); }
  internal void UpdateEdgeVisibility(bool leftHeld, Point pointer) {
   UpdateEdgeVisibility(leftHeld,pointer,mouseDrag.IsDragging);
  }
  internal void UpdateEdgeVisibility(bool leftHeld, Point pointer, bool dragDetected) {
   if (!leftHeld || !AtEdge(pointer,Screen.FromPoint(pointer).Bounds,preferences.EdgeSide,preferences.EdgePercent)) edgeReactivationBlocked = false;
   if (edgePreview && Visible) {
    if (leftHeld) {
     int distance = DistanceFromWindow(pointer);
     if (Bounds.Contains(pointer)) { edgeDragEnteredWindow = true; edgeDragDismissed = false; edgeDragClosestDistance = 0; CancelEmptyShelfClose(); if (animationTo == 0) BeginFade(true); }
     else if (edgeDragDismissed || edgeDragEnteredWindow || distance > edgeDragClosestDistance) { edgeDragDismissed = true; edgeReactivationBlocked = true; CancelEmptyShelfClose(); BeginFade(false); }
     else { edgeDragClosestDistance = distance; CancelEmptyShelfClose(); }
     return;
    }
    // Releasing the mouse ends preview tracking; leaving the edge no longer hides it.
    edgePreview = false;
    return;
   }
   if (!Visible) edgePreview = false;
   if (preferences.EdgeEnabled && !Visible && leftHeld && dragDetected && !edgeReactivationBlocked && AtEdge(pointer,Screen.FromPoint(pointer).Bounds,preferences.EdgeSide,preferences.EdgePercent)) {
    var area = Screen.FromPoint(pointer).WorkingArea; Location = new Point(preferences.EdgeSide == "left" ? area.Left : area.Right-Width, area.Top+Math.Max(0,(area.Height-Height)/2));
    edgeDragEnteredWindow = Bounds.Contains(pointer); edgeDragClosestDistance = DistanceFromWindow(pointer); edgeDragDismissed = false;
    edgePreview = true; BeginFade(true);
   }
  }
  int DistanceFromWindow(Point p) { return Math.Max(0,Math.Max(Left-p.X,p.X-(Right-1))) + Math.Max(0,Math.Max(Top-p.Y,p.Y-(Bottom-1))); }
  internal Preferences Settings { get { return preferences; } }
  static void DrawPin(Graphics g, Rectangle bounds, bool active) { var state = g.Save(); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.TranslateTransform(bounds.Width/2F,bounds.Height/2F); var color = active ? Color.FromArgb(0,122,111) : Color.FromArgb(107,120,126); using (var pen = new Pen(color,1.5F)) { pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round; pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawLine(pen,-4,-7,4,-7); g.DrawLine(pen,-3,-7,-3,-1); g.DrawLine(pen,3,-7,3,-1); g.DrawLine(pen,-3,-1,-5,2); g.DrawLine(pen,3,-1,5,2); g.DrawLine(pen,-5,2,5,2); g.DrawLine(pen,0,2,0,7); } g.Restore(state); }
  internal void SavePreferences() { try { var path = Path.Combine(root,"settings.json"); var temp = path + ".tmp"; using (var fs = File.Create(temp)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(fs,preferences); if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); } catch (Exception ex) { MessageBox.Show("无法保存设置：" + ex.Message,"拾放"); } }
  void LoadPreferences() { var path = Path.Combine(root,"settings.json"); if (!File.Exists(path)) return; try { using (var fs = File.OpenRead(path)) { var saved = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(fs); if (saved != null) preferences = saved; } if (preferences.EdgeSide != "left" && preferences.EdgeSide != "right") preferences.EdgeSide = "right"; if (preferences.EdgePercent < 5 || preferences.EdgePercent > 50 || preferences.EdgePercent % 5 != 0) preferences.EdgePercent = 10; if (preferences.EmptyCloseDelayHalfSeconds < 0 || preferences.EmptyCloseDelayHalfSeconds > 4) preferences.EmptyCloseDelayHalfSeconds = 1; } catch { preferences = new Preferences(); } }
  internal void ShowSettings() { edgePreview = false; CancelEmptyShelfClose(); BeginFade(true); using (var dialog = new Form { Text = "拾放设置", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(370,378), Font = Font, ShowInTaskbar = false }) { var enabled = new CheckBox { Text = "拖拽到屏幕边缘时显示拾放", AutoSize = true, Location = new Point(22,22), Checked = preferences.EdgeEnabled }; var label = new Label { Text = "屏幕边缘", AutoSize = true, Location = new Point(22,65) }; var left = new RadioButton { Text = "左侧", AutoSize = true, Location = new Point(100,63), Checked = preferences.EdgeSide == "left" }; var right = new RadioButton { Text = "右侧", AutoSize = true, Location = new Point(180,63), Checked = preferences.EdgeSide != "left" }; var widthLabel = new Label { Text = "边缘范围", AutoSize = true, Location = new Point(22,105) }; var width = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(100,101), Width = 85 }; for (int n = 5; n <= 50; n += 5) width.Items.Add(n + "%"); width.SelectedIndex = preferences.EdgePercent / 5 - 1; var unit = new Label { Text = "屏幕宽度", AutoSize = true, Location = new Point(194,105) }; var emptyDelayLabel = new Label { Text = "最后一项移除后，离开窗口", AutoSize = true, Location = new Point(22,148) }; var emptyDelay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(220,144), Width = 110 }; emptyDelay.Items.Add("立即关闭"); for (int n = 1; n <= 4; n++) emptyDelay.Items.Add((n * 0.5).ToString("0.0") + " 秒"); emptyDelay.SelectedIndex = preferences.EmptyCloseDelayHalfSeconds; var closeLabel = new Label { Text = "关闭窗口时", AutoSize = true, Location = new Point(22,190) }; var closeMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130,186), Width = 175 }; closeMode.Items.Add("直接退出程序"); closeMode.Items.Add("收起到托盘"); closeMode.SelectedIndex = preferences.CloseToTray ? 1 : 0; preferences.StartWithWindows = StartupRegistration.IsEnabled(); var startup = new CheckBox { Text = "开机时静默启动到托盘", AutoSize = true, Location = new Point(22,233), Checked = preferences.StartWithWindows }; var note = new Label { Text = "便携版请保持程序文件位置不变。", AutoSize = true, Location = new Point(42,258), ForeColor = muted }; Action update = () => { left.Enabled = enabled.Checked; right.Enabled = enabled.Checked; width.Enabled = enabled.Checked; }; enabled.CheckedChanged += (s,e) => update(); update(); var ok = new Button { Text = "保存", Location = new Point(192,313), Size = new Size(75,30), DialogResult = DialogResult.OK }; var cancel = new Button { Text = "取消", Location = new Point(274,313), Size = new Size(75,30), DialogResult = DialogResult.Cancel }; dialog.Controls.AddRange(new Control[] { enabled,label,left,right,widthLabel,width,unit,emptyDelayLabel,emptyDelay,closeLabel,closeMode,startup,note,ok,cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel; if (dialog.ShowDialog(this) == DialogResult.OK) { try { StartupRegistration.Set(startup.Checked, Application.ExecutablePath); preferences.StartWithWindows = startup.Checked; } catch (Exception ex) { MessageBox.Show("无法更改开机启动设置：" + ex.Message, "拾放", MessageBoxButtons.OK, MessageBoxIcon.Error); return; } preferences.EdgeEnabled = enabled.Checked; preferences.EdgeSide = left.Checked ? "left" : "right"; preferences.EdgePercent = (width.SelectedIndex + 1) * 5; preferences.EmptyCloseDelayHalfSeconds = emptyDelay.SelectedIndex; preferences.CloseToTray = closeMode.SelectedIndex == 1; SavePreferences(); } } }
  internal void ShowShelf() { edgePreview = false; if (!Visible) { var area = Screen.FromPoint(Cursor.Position).WorkingArea; if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Bounds))) Location = new Point(preferences.EdgeSide == "left" ? area.Left+16 : area.Right-Width-16,area.Top+Math.Max(10,(area.Height-Height)/2)); } BeginFade(true); WindowState = FormWindowState.Normal; Activate(); }
  public new void Hide() { edgePreview = false; CancelEmptyShelfClose(); BeginFade(false); }
  protected override void SetVisibleCore(bool value) { if (value && startHidden && !initialVisibilityHandled) { initialVisibilityHandled = true; var handle = Handle; base.SetVisibleCore(false); return; } if (value) initialVisibilityHandled = true; base.SetVisibleCore(value); }
  readonly System.Diagnostics.Stopwatch animationClock = new System.Diagnostics.Stopwatch();
  void BeginFade(bool show) {
   if (show) CancelEmptyShelfClose();
   if (IsDisposed || (animation.Enabled && animationTo == (show ? 1 : 0))) return;
   animation.Stop(); animationFrom = Visible ? Opacity : 0; animationTo = show ? 1 : 0;
   if (show && !Visible) { Opacity = 0; ShowInTaskbar = true; base.Show(); }
   if (!show && !Visible) { edgePreview = false; Opacity = 1; return; }
   if (Math.Abs(animationFrom-animationTo) < 0.001) { FinishAnimation(); return; }
   animationClock.Restart(); animation.Start();
  }
  void AdvanceAnimation() { double progress = Math.Min(1.0,animationClock.Elapsed.TotalMilliseconds/180.0); double eased = progress*progress*(3-2*progress); Opacity = Math.Max(0,Math.Min(1,animationFrom+(animationTo-animationFrom)*eased)); if (progress >= 1) FinishAnimation(); }
  void FinishAnimation() { animation.Stop(); animationClock.Stop(); Opacity = animationTo; if (animationTo == 0) { edgePreview = false; CancelEmptyShelfClose(); base.Hide(); Opacity = 1; } }
  void CancelEmptyShelfClose() { emptyCloseTimer.Stop(); emptyCloseClock.Reset(); emptyShelfClosePending = false; }
  void CheckEmptyShelfClose() {
   if (emptyShelfClosePending) {
    if (entries.Count > 0 || !Visible) { CancelEmptyShelfClose(); return; }
    if (Bounds.Contains(PointerPosition())) {
     emptyCloseClock.Reset();
     if (animation.Enabled && animationTo == 0) { BeginFade(true); emptyShelfClosePending = true; emptyCloseTimer.Start(); }
     return;
    }
    if (!emptyCloseClock.IsRunning) emptyCloseClock.Restart();
    int emptyRemaining = preferences.EmptyCloseDelayHalfSeconds * 500 - (int)emptyCloseClock.ElapsedMilliseconds;
    if (emptyRemaining <= 0) BeginFade(false); else emptyCloseTimer.Interval = Math.Min(50,emptyRemaining);
    return;
   }
   CancelEmptyShelfClose();
  }
  internal void StopEdgePollingForTests() { edge.Stop(); }
  internal void CompleteAnimationForTests() { if (animation.Enabled) FinishAnimation(); }
  Button Button(string text, string tip, int x, int y) { var b = new Button { Text = text, Size = new Size(30,30), Location = new Point(x,y), FlatStyle = FlatStyle.Flat, ForeColor = muted, BackColor = Color.White, Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = 0; tips.SetToolTip(b, tip); b.AccessibleName = tip; return b; }
  void EnterDrop(object s, DragEventArgs e) { e.Effect = !ownDrag && CanAccept(e.Data) && (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None; }
  bool CanAccept(IDataObject data) { return data != null && (data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Bitmap)); }
  void Drop(object s, DragEventArgs e) { if (!ownDrag) AcceptDrop(e.Data); }
  internal void AcceptDrop(IDataObject data) { if (!CanAccept(data)) return; edgePreview = false; BeginFade(true); AddData(data); }
  void Paste() { try { AddData(Clipboard.GetDataObject()); } catch (Exception ex) { MessageBox.Show("无法读取剪贴板：" + ex.Message, "拾放"); } }
  internal void AddData(IDataObject data) {
   if (data == null) return;
   try {
    if (data.GetDataPresent(DataFormats.FileDrop)) { var paths = ((string[])data.GetData(DataFormats.FileDrop)).Where(path => File.Exists(path) || Directory.Exists(path)).ToArray(); if (paths.Length == 1) entries.Add(new Entry { Kind = "file", Value = paths[0], Name = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar)) }); else if (paths.Length > 1) entries.Add(new Entry { Kind = "group", Value = "", Paths = paths, Name = "文件合集（" + paths.Length + " 项）" }); }
    else if (data.GetDataPresent(DataFormats.Bitmap)) { using (var image = (Image)data.GetData(DataFormats.Bitmap)) { var dir = Path.Combine(root,"images"); Directory.CreateDirectory(dir); var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".png"); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); entries.Add(new Entry { Kind = "image", Value = path, Name = "剪贴板图片 " + DateTime.Now.ToString("MM-dd HH:mm") }); } }
    else if (data.GetDataPresent(DataFormats.UnicodeText)) { var t = (string)data.GetData(DataFormats.UnicodeText); if (!String.IsNullOrWhiteSpace(t)) entries.Add(new Entry { Kind = "text", Value = t, Name = t.Replace("\r", " ").Replace("\n", " ") }); }
    if (entries.Count > 0 && emptyShelfClosePending) { CancelEmptyShelfClose(); BeginFade(true); }
    Save(); RefreshItems();
   } catch (Exception ex) { MessageBox.Show("添加内容失败：" + ex.Message, "拾放"); }
  }
  void RefreshItems() {
   if (list.IsDisposed) return; list.SuspendLayout(); foreach (Control c in list.Controls.Cast<Control>().ToArray()) { list.Controls.Remove(c); if (c != empty) c.Dispose(); } selected.RemoveWhere(x => !entries.Contains(x)); count.Text = entries.Count + " 项暂存 · 拖出后移除";
   if (entries.Count == 0) list.Controls.Add(empty);
   foreach (var entry in entries.ToArray()) {
    var item = new Panel { Width = Math.Max(260, list.ClientSize.Width - 29), Height = 72, BackColor = Color.White, Margin = new Padding(0,0,0,8), Cursor = Cursors.Hand, Tag = entry, AllowDrop = true };
    var icon = new Label { Text = entry.Kind == "group" ? "▦" : entry.Kind == "text" ? "T" : entry.Kind == "image" ? "▧" : "▤", Size = new Size(42,42), Location = new Point(12,15), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(225,244,239), ForeColor = accent, Font = new Font("Segoe UI", 17F), Tag = entry, Cursor = Cursors.Hand };
    var name = new Label { Text = entry.Name, AutoEllipsis = true, Location = new Point(64,12), Size = new Size(Math.Max(130,item.Width-110),24), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), ForeColor = ink, Tag = entry, Cursor = Cursors.Hand };
    var detail = new Label { Text = entry.Kind == "group" ? String.Join("、", entry.Paths.Select(p => Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar)))) : entry.Kind == "text" ? "文字 / 链接" : entry.Kind == "image" ? "图片" : (File.Exists(entry.Value) || Directory.Exists(entry.Value) ? "文件 / 文件夹" : "文件不存在"), AutoEllipsis = true, Location = new Point(64,39), Size = new Size(Math.Max(130,item.Width-110),20), ForeColor = muted, Tag = entry, Cursor = Cursors.Hand };
    var remove = Button("×", "移除", item.Width-38, 19); remove.Anchor = AnchorStyles.Top | AnchorStyles.Right; remove.Click += (s,e) => RemoveEntries(new[]{entry});
    var context = new ContextMenuStrip(); context.Items.Add("复制",null,(s,e) => CopyEntry(entry)); if (entry.Kind == "group") context.Items.Add("查看合集内容",null,(s,e) => MessageBox.Show(String.Join("\n",entry.Paths),"合集内容")); if (entry.Kind != "text" && entry.Kind != "group") context.Items.Add("在资源管理器中显示",null,(s,e) => { if (File.Exists(entry.Value)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + entry.Value + "\""); else if (Directory.Exists(entry.Value)) System.Diagnostics.Process.Start("explorer.exe", "\"" + entry.Value + "\""); else MessageBox.Show("原文件已移动或删除。", "拾放"); }); context.Items.Add("移除暂存",null,(s,e) => RemoveEntries(new[]{entry})); item.Disposed += (s,e) => context.Dispose();
    foreach (Control c in new Control[]{item, icon, name, detail}) { c.MouseDown += ItemDown; c.MouseMove += ItemMove; c.DoubleClick += (s,e) => CopyEntry(entry); c.DragEnter += EnterDrop; c.DragDrop += Drop; c.ContextMenuStrip = context; }
    item.Controls.AddRange(new Control[]{icon,name,detail,remove}); list.Controls.Add(item);
    if (entry.Kind == "image" || (entry.Kind == "file" && new[]{".png",".jpg",".jpeg",".bmp",".gif"}.Contains(Path.GetExtension(entry.Value).ToLowerInvariant()))) {
     try { if (new FileInfo(entry.Value).Length < 32*1024*1024) using (var original = Image.FromFile(entry.Value)) { var thumb = new Bitmap(42,42); using (var g = Graphics.FromImage(thumb)) { g.Clear(Color.FromArgb(225,244,239)); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; float scale = Math.Min(42F/original.Width,42F/original.Height); int w = Math.Max(1,(int)(original.Width*scale)), h = Math.Max(1,(int)(original.Height*scale)); g.DrawImage(original,(42-w)/2,(42-h)/2,w,h); } icon.Text = ""; icon.Image = thumb; icon.Disposed += (s,e) => thumb.Dispose(); } } catch {}
    }
   } PaintSelection(); list.ResumeLayout();
  }
  void PaintSelection() { foreach (Control c in list.Controls) if (c.Tag is Entry) { Color color = selected.Contains((Entry)c.Tag) ? Color.FromArgb(222,241,237) : Color.White; c.BackColor = color; foreach (Control child in c.Controls) if (child is Button || (child is Label && child.Width > 50)) child.BackColor = color; } }
  void ItemDown(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { var entry = (Entry)((Control)s).Tag; if ((ModifierKeys & Keys.Control) != 0) { if (!selected.Add(entry)) selected.Remove(entry); } else if (!selected.Contains(entry)) { selected.Clear(); selected.Add(entry); } PaintSelection(); dragging = true; dragStart = Cursor.Position; Focus(); } }
  void ItemMove(object s, MouseEventArgs e) {
   if (!dragging || e.Button != MouseButtons.Left || selected.Count == 0) return;
   var p = Cursor.Position; if (Math.Abs(p.X-dragStart.X) < 5 && Math.Abs(p.Y-dragStart.Y) < 5) return;
   dragging = false; var source = (Control)s; var dragged = selected.ToArray();
   try {
    var data = MakeSelectionData(dragged); ownDrag = true;
    var result = source.DoDragDrop(data, DragDropEffects.Copy);
    CompleteOutboundDrag(result, dragged);
   } catch (Exception ex) { MessageBox.Show("拖出失败：" + ex.Message, "拾放"); }
   finally { ownDrag = false; }
  }
  internal void CompleteOutboundDrag(DragDropEffects result, Entry[] dragged) { if ((result & DragDropEffects.Copy) == 0) return; foreach (var entry in dragged) { entries.Remove(entry); selected.Remove(entry); } Save(); RefreshItems(); if (entries.Count == 0) Hide(); }
  internal void RemoveEntries(Entry[] removed) { bool changed = false; foreach (var entry in removed) { changed |= entries.Remove(entry); selected.Remove(entry); } if (!changed) return; Save(); RefreshItems(); if (entries.Count == 0) ScheduleEmptyShelfClose(); }
  void ScheduleEmptyShelfClose() { edgePreview = false; CancelEmptyShelfClose(); emptyShelfClosePending = true; emptyCloseTimer.Interval = 50; emptyCloseTimer.Start(); CheckEmptyShelfClose(); }
  internal DataObject MakeSelectionData(Entry[] items) { if (items.Length == 1 && items[0].Kind != "group") return MakeData(items[0]); if (items.All(x => x.Kind == "text")) { var data = new DataObject(); data.SetData(DataFormats.UnicodeText, String.Join(Environment.NewLine,items.Select(x => x.Value))); return data; } if (items.All(x => x.Kind != "text")) { var paths = items.SelectMany(x => x.Kind == "group" ? x.Paths : new[]{x.Value}).ToArray(); foreach (var path in paths) if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("原文件已移动或删除：" + path); var data = new DataObject(); data.SetData(DataFormats.FileDrop,paths); return data; } throw new InvalidOperationException("请选择同类内容：文件与图片可一起拖出，多段文字可一起拖出。 "); }
  DataObject MakeData(Entry e) { var d = new DataObject(); if (e.Kind == "text") { d.SetData(DataFormats.UnicodeText,e.Value); Uri uri; bool url = Uri.TryCreate(e.Value.Trim(),UriKind.Absolute,out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && e.Value.IndexOf('\n') < 0; var dir = Path.Combine(root,"exports"); Directory.CreateDirectory(dir); string hash; using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(e.Value))).Replace("-", "").Substring(0,16); string path = Path.Combine(dir,(url ? "链接-" : "文字-") + hash + (url ? ".url" : ".txt")); File.WriteAllText(path,url ? "[InternetShortcut]\r\nURL=" + uri.AbsoluteUri + "\r\n" : e.Value, new System.Text.UTF8Encoding(true)); d.SetData(DataFormats.FileDrop,new[]{path}); if (url) d.SetData("UniformResourceLocatorW", new MemoryStream(System.Text.Encoding.Unicode.GetBytes(uri.AbsoluteUri + "\0"))); } else { if (!File.Exists(e.Value) && !Directory.Exists(e.Value)) throw new IOException("原文件已移动或删除。 "); d.SetData(DataFormats.FileDrop, new string[]{e.Value}); if (e.Kind == "image") { using (var image = Image.FromFile(e.Value)) d.SetData(DataFormats.Bitmap,new Bitmap(image)); } } return d; }
  void CopyEntry(Entry e) { try { Clipboard.SetDataObject(MakeSelectionData(new[]{e}), true); } catch (Exception ex) { MessageBox.Show("复制失败：" + ex.Message, "拾放"); } }
  void LoadItems() { var path = Path.Combine(root,"items.json"); if (!File.Exists(path)) return; try { using (var fs = File.OpenRead(path)) { var serializer = new DataContractJsonSerializer(typeof(List<Entry>)); var saved = (List<Entry>)serializer.ReadObject(fs); if (saved != null) entries.AddRange(saved.Where(x => x != null && x.Value != null && x.Name != null && (x.Kind == "text" || x.Kind == "file" || x.Kind == "image" || (x.Kind == "group" && x.Paths != null && x.Paths.Length > 0 && x.Paths.All(p => !String.IsNullOrEmpty(p)))))); } } catch { var backup = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"); File.Copy(path, backup, true); MessageBox.Show("暂存列表无法读取，已备份到 " + backup, "拾放"); } }
  internal Entry[] Items { get { return entries.ToArray(); } }
  protected override void Dispose(bool disposing) { if (disposing) { mouseDrag.Dispose(); edge.Stop(); edge.Dispose(); animation.Stop(); animation.Dispose(); emptyCloseTimer.Stop(); emptyCloseTimer.Dispose(); tray.Visible = false; tray.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
  void Save() { try { var path = Path.Combine(root,"items.json"); var temp = path + ".tmp"; using (var fs = File.Create(temp)) new DataContractJsonSerializer(typeof(List<Entry>)).WriteObject(fs, entries); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp,path); } catch (Exception ex) { MessageBox.Show("无法保存暂存列表：" + ex.Message, "拾放"); } }
 }
}
